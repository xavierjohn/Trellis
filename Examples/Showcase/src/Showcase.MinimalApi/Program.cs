using FluentValidation;
using Mediator;
using Scalar.AspNetCore;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Idempotency;
using Trellis.Asp.Routing;
using Trellis.FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;
using Trellis.Showcase.Application;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Services;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;
using Trellis.Showcase.MinimalApi;
using Trellis.Showcase.MinimalApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ShowcaseJsonSerializerContext.Default);
});
builder.Services.AddTrellisAspWithScalarValidation();
builder.Services.AddTrellisRouteConstraint<AccountId>();
// AccountType, AccountStatus, and TransactionType are RequiredEnum<TSelf> value objects;
// each carries its own [JsonConverter(typeof(RequiredEnumJsonConverter<T>))] attribute
// (added by the Trellis source generator), so no JsonStringEnumConverter<T> registration
// is needed — the wire format is still the symbolic name (e.g., "Active").
builder.Services.AddOpenApi();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
builder.Services.AddSingleton<IFraudGateway, InMemoryFraudGateway>();
builder.Services.AddSingleton<IIdentityVerifier, InMemoryIdentityVerifier>();
builder.Services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
builder.Services.AddScoped<BankingWorkflow>();

// Mediator pipeline: AddTrellisBehaviors() registers the canonical
// (Exception, Tracing, Logging, Authorization, Validation) stack.
// AddTrellisFluentValidation() plugs the open-generic FluentValidation adapter into the
// Validation stage via IMessageValidator<>, so IValidate failures and FluentValidation
// failures aggregate into a single response (no second behavior slot, AOT-friendly).
builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<SubmitBatchTransfersCommand>, SubmitBatchTransfersValidator>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentActorProvider();
builder.Services.AddAuthorization();

// Opt-in IETF Idempotency-Key middleware. Endpoints opt in per-method by carrying
// [Idempotent] metadata; everything else is unaffected. The in-memory store is fine
// for samples — production hosts would register a distributed store implementation.
builder.Services.AddTrellisIdempotency();
builder.Services.AddInMemoryIdempotencyStore();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    ShowcaseSeed.Apply(repo, timeProvider);
}

// The description link is advertised only where the document is actually mapped. Advertising a
// service-desc relation that resolves to 404 would be worse than omitting it, so the URL and the
// route that serves it are turned on by the same condition.
var apiDescriptionUrl = app.Environment.IsDevelopment() ? "/openapi/v1.json" : null;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseScalarValueValidation();
app.UseAuthorization();
app.UseTrellisIdempotency();

app.MapAccountEndpoints(apiDescriptionUrl);
app.MapTransferEndpoints();
app.MapBatchTransferEndpoints();
app.MapInterestEndpoints();
app.MapDiagnosticsEndpoints();

app.Run();

namespace Trellis.Showcase.MinimalApi
{
    /// <summary>Marker class for WebApplicationFactory&lt;T&gt;.</summary>
    public partial class Program;
}