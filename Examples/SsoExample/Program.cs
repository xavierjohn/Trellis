using Microsoft.AspNetCore.Authentication.JwtBearer;
using Trellis.Asp.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Trellis actor provider + authentication
if (builder.Environment.IsDevelopment())
{
    // Development: no JWT validation — DevelopmentActorProvider reads X-Test-Actor header
    builder.Services.AddDevelopmentActorProvider();
}
else
{
    // Production: JWT bearer authentication + claims-based actor resolution
    var authSection = builder.Configuration.GetSection("Authentication");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authSection["Authority"];
            options.Audience = authSection["Audience"];
        });

    builder.Services.AddClaimsActorProvider(options =>
    {
        var authSection = builder.Configuration.GetSection("Authentication");
        options.ActorIdClaim = authSection["ActorIdClaim"] ?? "sub";
        options.PermissionsClaim = authSection["PermissionsClaim"] ?? "permissions";
    });
}

builder.Services.AddAuthorization(options =>
{
    if (!builder.Environment.IsDevelopment())
    {
        // Production: require authenticated user on all endpoints by default
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
});
builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseAuthentication();

app.UseAuthorization();
app.MapControllers();

app.Run();
