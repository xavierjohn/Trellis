using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Idempotency;
using Trellis.Asp.Routing;
using Trellis.Showcase.Application;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Services;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrellisAspWithScalarValidation();

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Trellis' ToHttpResponse() returns IResult, which executes via HttpContext and
// reads ConfigureHttpJsonOptions (not MVC's AddJsonOptions). Configure both so
// MVC formatters and IResult-based responses serialize enums identically.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddTrellisRouteConstraint<AccountId>();

// ApiExplorer derives each operation's media types from the configured formatters, so MVC
// described every response as also available in text/plain and text/json, and every request body
// as also accepting text/json and application/*+json. This API produces and accepts none of
// those: Trellis' ToHttpResponse() writes JSON through IResult, so the description was
// advertising formatter capability rather than actual behaviour, and it disagreed with the
// Minimal API document for no real reason.
//
// Trim the formatters rather than applying [Produces("application/json")]. [Produces] is a result
// filter that overwrites ObjectResult.ContentTypes wholesale, which also rewrites the automatic
// model-validation 422 from application/problem+json to application/json -- silently breaking
// RFC 9457 for the one response that still goes through MVC's formatter pipeline.
builder.Services.PostConfigure<MvcOptions>(o =>
{
    o.OutputFormatters.RemoveType<StringOutputFormatter>();

    foreach (var formatter in o.OutputFormatters.OfType<SystemTextJsonOutputFormatter>())
        formatter.SupportedMediaTypes.Remove("text/json");

    foreach (var formatter in o.InputFormatters.OfType<SystemTextJsonInputFormatter>())
    {
        formatter.SupportedMediaTypes.Remove("text/json");
        formatter.SupportedMediaTypes.Remove("application/*+json");
    }
});

builder.Services.AddOpenApi();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
builder.Services.AddSingleton<IFraudGateway, InMemoryFraudGateway>();
builder.Services.AddSingleton<IIdentityVerifier, InMemoryIdentityVerifier>();
builder.Services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
builder.Services.AddScoped<BankingWorkflow>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentActorProvider();
builder.Services.AddAuthorization();

// Opt-in IETF Idempotency-Key middleware. Endpoints opt in per-action by carrying
// [Idempotent]; everything else is unaffected. The in-memory store is fine for
// samples — production hosts would register a distributed store implementation.
builder.Services.AddTrellisIdempotency();
builder.Services.AddInMemoryIdempotencyStore();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    ShowcaseSeed.Apply(repo, timeProvider);
}

// The API description is served in every environment, which is what lets AccountsController
// advertise it unconditionally via `service-desc`: a relation is a promise a client can follow,
// so the document has to exist wherever the link is emitted. The interactive UI stays
// development-only — that is a convenience, not part of the contract.
app.MapOpenApi();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.UseScalarValueValidation();
app.UseAuthorization();
app.UseTrellisIdempotency();
app.MapControllers();

app.Run();

namespace Trellis.Showcase.Mvc
{
    /// <summary>Marker class for WebApplicationFactory&lt;T&gt;.</summary>
    public partial class Program;
}