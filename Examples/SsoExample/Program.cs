using Microsoft.AspNetCore.Authentication.JwtBearer;
using Trellis.Asp.Authorization;

var builder = WebApplication.CreateBuilder(args);

var authSection = builder.Configuration.GetSection("Authentication");

// Authentication — configure one or more OIDC providers
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authSection["Authority"];
        options.Audience = authSection["Audience"];
    });

// Trellis actor provider — reads standard OIDC claims
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevelopmentActorProvider();
}
else
{
    builder.Services.AddClaimsActorProvider(options =>
    {
        options.ActorIdClaim = authSection["ActorIdClaim"] ?? "sub";
        options.PermissionsClaim = authSection["PermissionsClaim"] ?? "permissions";
    });
}

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
