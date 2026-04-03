using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SampleDataAccess;
using SampleMinimalApiNoAot.API;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// NO JsonSerializerContext - uses standard JSON serialization with reflection fallback
// This demonstrates that the library works perfectly without source generation!
builder.Services.AddScalarValueValidationForMinimalApi();

// EF Core with in-memory SQLite (shared-cache for thread-safe parallel queries)
var connection = new SqliteConnection("DataSource=SampleNoAot;Mode=Memory;Cache=Shared");
connection.Open();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connection)
           .AddTrellisInterceptors());
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("DataSource=SampleNoAot;Mode=Memory;Cache=Shared")
           .AddTrellisInterceptors(), ServiceLifetime.Scoped);

// Dispose SQLite connection on shutdown
builder.Services.AddSingleton(connection);
builder.Services.AddHostedService<SqliteConnectionDisposer>();

// Authorization — DevelopmentActorProvider reads actor from X-Test-Actor header
builder.Services.AddDevelopmentActorProvider();

builder.Services.AddAuthorization();

// Register domain services
builder.Services.AddSingleton<IPaymentService, FakePaymentService>();
builder.Services.AddSingleton<INotificationService, FakeNotificationService>();

Action<ResourceBuilder> configureResource = r => r.AddService(
    serviceName: "SampleMinimalApiNoAot",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(configureResource)
    .WithTracing(tracing
        => tracing.AddSource("SampleMinimalApiNoAot")
            .SetSampler(new AlwaysOnSampler())
            .AddPrimitiveValueObjectInstrumentation()
            .AddOtlpExporter());

var app = builder.Build();

// Create schema and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await DatabaseSeeder.SeedAsync(db);
}

app.UseScalarValueValidation();
app.UseAuthorization();

// Welcome endpoint with API information
#pragma warning disable CA1861 // Prefer 'static readonly' fields - one-time startup configuration
app.MapGet("/", () => Results.Ok(new
{
    name = "FunctionalDDD Sample Minimal API (No AOT)",
    version = "2.0.0",
    description = "Demonstrates full Trellis Framework with ROP, EF Core, RFC 9110, and authorization",
    endpoints = new
    {
        users = new
        {
            register = "POST /users/register - Register user with manual validation (Result.Combine)",
            registerCreated = "POST /users/registerCreated - Register user returning 201 Created",
            registerAutoValidation = "POST /users/RegisterWithAutoValidation - Auto-validation (Maybe<Url>)"
        },
        products = new
        {
            list = "GET /products?page=0&pageSize=25&inStock=true&minPrice=50&maxPrice=200 - Paginated (RFC 9110 §14: 206 Partial Content)",
            getById = "GET /products/{id} - Conditional GET (If-None-Match → 304 Not Modified)",
            create = "POST /products - Create with ETag + Location",
            update = "PUT /products/{id} - Update with If-Match (Prefer: return=minimal → 204)",
            delete = "DELETE /products/{id} - Delete (204 No Content)",
            legacyRedirect = "GET /products/legacy/{id} - 301 Moved Permanently redirect"
        },
        orders = new
        {
            create = "POST /orders - Create order (async BindAsync chain)",
            getById = "GET /orders/{id} - Get with ETag conditional GET",
            confirm = "POST /orders/{id}/confirm - Confirm (EnsureAsync + BindAsync + TapAsync + auth)",
            cancel = "POST /orders/{id}/cancel - Cancel (RecoverOnFailureAsync cleanup)",
            receipt = "POST /orders/{id}/receipt - 303 See Other redirect",
            states = "GET /orders/states - All order states (RequiredEnum demo)",
            stateByName = "GET /orders/states/{state} - State model binding"
        },
        dashboard = "GET /dashboard - ParallelAsync/WhenAllAsync concurrent data fetch",
        authorization = "Set X-Test-Actor header: {\"id\":\"user1\",\"permissions\":[\"orders:write\"]}"
    },
    documentation = "See SampleApi.http for complete API examples"
})).WithName("Welcome");
#pragma warning restore CA1861

app.UseUserRoute();
app.UseMoneyRoute();
app.UseOrderRoute();
app.UseProductRoute();
app.UseNewOrderRoute();
app.UseDashboardRoute();
app.Run();

#pragma warning disable CA1050 // Declare types in namespaces
public record SharedNameTypeResponse(string FirstName, string LastName, string Email, string Message);
#pragma warning restore CA1050 // Declare types in namespaces

/// <summary>Disposes the in-memory SQLite connection on application shutdown.</summary>
internal sealed class SqliteConnectionDisposer(SqliteConnection connection) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) { connection.Dispose(); return Task.CompletedTask; }
}

// NO [GenerateScalarValueConverters] attribute
// NO JsonSerializerContext
// Uses standard reflection-based JSON serialization - works perfectly!