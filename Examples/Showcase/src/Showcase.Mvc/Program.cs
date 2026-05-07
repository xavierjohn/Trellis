using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Routing;
using Trellis.Showcase.Application;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Services;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrellisAsp();

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    ShowcaseSeed.Apply(repo, timeProvider);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseScalarValueValidation();
app.UseAuthorization();
app.MapControllers();

app.Run();

namespace Trellis.Showcase.Mvc
{
    /// <summary>Marker class for WebApplicationFactory&lt;T&gt;.</summary>
    public partial class Program;
}
