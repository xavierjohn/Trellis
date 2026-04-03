using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using SampleUserLibrary;
using Scalar.AspNetCore;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddScalarValueValidation(); // ← Enables automatic scalar value validation

builder.Services.AddOpenApi();

// EF Core with in-memory SQLite (shared-cache for thread-safe parallel queries)
var connection = new SqliteConnection("DataSource=SampleMvc;Mode=Memory;Cache=Shared");
connection.Open();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connection)
           .AddTrellisInterceptors());
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("DataSource=SampleMvc;Mode=Memory;Cache=Shared")
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

var app = builder.Build();

// Create schema and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await DatabaseSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseScalarValueValidation(); // ← Must be before routing for validation error collection

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Disposes the in-memory SQLite connection on application shutdown.</summary>
internal sealed class SqliteConnectionDisposer(SqliteConnection connection) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) { connection.Dispose(); return Task.CompletedTask; }
}