using ConditionalRequestExample.Api;
using ConditionalRequestExample.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite in-memory: keep a single open connection so the database survives across requests.
var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSqlite(connection)
           .AddTrellisInterceptors());

builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();

app.UseScalarValueValidation();

// Create the schema on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    db.Database.EnsureCreated();
}

app.MapOptionalETagRoutes();
app.MapRequiredETagRoutes();
app.Run();