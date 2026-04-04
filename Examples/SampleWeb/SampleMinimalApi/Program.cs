using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SampleDataAccess;
using SampleMinimalApi.API;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.EntityFrameworkCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
builder.Services.AddScalarValueValidationForMinimalApi();

// EF Core with in-memory SQLite (shared-cache for thread-safe parallel queries)
var connection = new SqliteConnection("DataSource=SampleAot;Mode=Memory;Cache=Shared");
connection.Open();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connection)
           .AddTrellisInterceptors());
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("DataSource=SampleAot;Mode=Memory;Cache=Shared")
           .AddTrellisInterceptors(), ServiceLifetime.Scoped);

// Dispose SQLite connection on shutdown
builder.Services.AddSingleton(connection);
builder.Services.AddHostedService<SqliteConnectionDisposer>();

// Authorization— DevelopmentActorProvider reads actor from X-Test-Actor header
if (builder.Environment.IsDevelopment())
builder.Services.AddDevelopmentActorProvider();

builder.Services.AddAuthorization();

// Register domain services
builder.Services.AddSingleton<IPaymentService, FakePaymentService>();
builder.Services.AddSingleton<INotificationService, FakeNotificationService>();

Action<ResourceBuilder> configureResource = r => r.AddService(
    serviceName: "SampleMinimalApi",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(configureResource)
    .WithTracing(tracing
        => tracing.AddSource("SampleMinimalApi")
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
app.MapGet("/", () => Results.Ok(new WelcomeResponse(
    Name: "FunctionalDDD Sample Minimal API (Source Generated)",
    Version: "2.0.0",
    Description: "Demonstrates full Trellis Framework with ROP, EF Core, RFC 9110, authorization, and source-generated JSON converters",
    Documentation: "See OrderApi.http for complete API examples"
))).WithName("Welcome");

app.UseUserRoute();
app.UseMoneyRoute();
app.UseOrderRoute();
app.UseProductRoute();
app.UseNewOrderRoute();
app.UseDashboardRoute();
app.Run();

#pragma warning disable CA1050 // Declare types in namespaces
public record SharedNameTypeResponse(string FirstName, string LastName, string Email, string Message);
public record WelcomeResponse(string Name, string Version, string Description, string Documentation);
#pragma warning restore CA1050 // Declare types in namespaces

/// <summary>Disposes the in-memory SQLite connection on application shutdown.</summary>
internal sealed class SqliteConnectionDisposer(SqliteConnection connection) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) { connection.Dispose(); return Task.CompletedTask; }
}

[GenerateScalarValueConverters]
[JsonSerializable(typeof(WelcomeResponse))]
[JsonSerializable(typeof(RegisterUserRequest))]
[JsonSerializable(typeof(RegisterUserDto))]
[JsonSerializable(typeof(RegisterWithNameDto))]
[JsonSerializable(typeof(SharedNameTypeResponse))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(Trellis.Primitives.Money))]
[JsonSerializable(typeof(Trellis.Primitives.Money[]))]
[JsonSerializable(typeof(SampleMinimalApi.API.MoneyDto))]
[JsonSerializable(typeof(SampleMinimalApi.API.CreateMoneyRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.MoneyOperationRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.MultiplyMoneyRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.MultiplyByQuantityRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.DivideMoneyRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.AllocateMoneyRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.CompareMoneyRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.CartTotalRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.ApplyDiscountRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.SplitBillRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.RevenueShareRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.RevenueShareResponse))]
[JsonSerializable(typeof(UpdateOrderDto))]
[JsonSerializable(typeof(CreateOrderDto))]
[JsonSerializable(typeof(OrderState))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderStateInfo))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderStatesResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderStateDetailResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.UpdateOrderResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.CustomerInfo))]
[JsonSerializable(typeof(SampleMinimalApi.API.CreateOrderResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.FilterOrdersResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.ProductResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.ProductResponse[]))]
[JsonSerializable(typeof(SampleMinimalApi.API.CreateProductRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.UpdateProductRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderLineResponse))]
[JsonSerializable(typeof(SampleMinimalApi.API.CreateOrderRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.OrderLineRequest))]
[JsonSerializable(typeof(SampleMinimalApi.API.DashboardResponse))]
[JsonSerializable(typeof(Product[]))]
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpResults.ValidationProblem))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}