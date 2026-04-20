using SampleMinimalApi.Endpoints;
using SampleMinimalApi.Persistence;
using SampleMinimalApi.Workflows;
using SampleUserLibrary;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

// JSON value-object validation for Minimal APIs (axiom A5 — wire-boundary 422 with FieldViolations).
builder.Services.AddScalarValueValidationForMinimalApi();
builder.Services.AddOpenApi();

// Time seam (axiom A7) — System.TimeProvider, no custom IClock.
builder.Services.AddSingleton(TimeProvider.System);

// In-memory repositories — singletons so state survives across requests within a process.
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// Domain services from SampleUserLibrary — fakes for the sample; swap in real impls in production.
builder.Services.AddSingleton<IPaymentService, FakePaymentService>();
builder.Services.AddSingleton<INotificationService, FakeNotificationService>();

// Workflows are scoped — one instance per request, holding the unit-of-work for that request.
builder.Services.AddScoped<UserWorkflow>();
builder.Services.AddScoped<ProductWorkflow>();
builder.Services.AddScoped<OrderWorkflow>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseScalarValueValidation();

app.MapUserEndpoints();
app.MapProductEndpoints();
app.MapOrderEndpoints();

app.Run();

namespace SampleMinimalApi
{
    /// <summary>Marker class for <c>WebApplicationFactory&lt;T&gt;</c> in integration tests.</summary>
    public partial class Program;
}
