using Scalar.AspNetCore;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddScalarValueValidation(); // ← Enables automatic scalar value validation

builder.Services.AddOpenApi();

var app = builder.Build();

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