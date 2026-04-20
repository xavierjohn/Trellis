namespace SampleMinimalApi.Endpoints;

using Microsoft.AspNetCore.Routing;
using SampleMinimalApi.Models;
using SampleMinimalApi.Persistence;
using SampleMinimalApi.Workflows;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;

/// <summary>
/// User endpoints. Each handler is a single expression: load + dispatch + render.
/// State-changing endpoints route through <see cref="UserWorkflow"/> (axiom A10).
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");

        group.MapPost("/", (RegisterUserDto dto, UserWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.RegisterAsync(dto, cancellationToken)
                .ToCreatedAtRouteHttpResultAsync(
                    "GetUser",
                    user => new RouteValueDictionary { ["userId"] = user.Id.Value },
                    UserResponse.From))
            .WithScalarValueValidation()
            .Produces<UserResponse>(StatusCodes.Status201Created);

        group.MapGet("/{userId}", (UserId userId, IUserRepository users, CancellationToken cancellationToken) =>
            users.GetAsync(userId, cancellationToken)
                .ToHttpResultAsync(UserResponse.From))
            .WithName("GetUser")
            .Produces<UserResponse>();

        return app;
    }
}
