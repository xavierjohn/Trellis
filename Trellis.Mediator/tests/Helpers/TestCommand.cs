namespace Trellis.Mediator.Tests.Helpers;

using global::Mediator;
using Trellis.Authorization;

/// <summary>
/// Command that implements <see cref="IValidate"/> for testing validation behavior.
/// </summary>
internal sealed record TestCommand(string Name)
    : ICommand<Result<string>>, IValidate
{
    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Name)
            ? Result.Fail<string>(Error.Validation("Name is required.", "Name"))
            : Result.Ok(Name);
}

/// <summary>
/// Command that does NOT implement <see cref="IValidate"/> (should skip validation behavior).
/// </summary>
internal sealed record TestCommandNoValidation(string Name)
    : ICommand<Result<string>>;

/// <summary>
/// Command with static permission-based authorization.
/// </summary>
internal sealed record AdminCommand(string Data)
    : ICommand<Result<string>>, IAuthorize, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["Admin.Write"];

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Data)
            ? Result.Fail<string>(Error.Validation("Data is required.", "Data"))
            : Result.Ok(Data);
}

/// <summary>
/// Command with multiple required permissions for testing missing-permission reporting.
/// </summary>
internal sealed record MultiPermissionCommand(string Data)
    : ICommand<Result<string>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Orders.Write"];
}

/// <summary>
/// Command with empty required permissions list (should always pass authorization).
/// </summary>
internal sealed record NoPermissionsCommand(string Data)
    : ICommand<Result<string>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => [];
}

/// <summary>
/// Query that implements <see cref="IValidate"/> for testing validation with queries.
/// </summary>
internal sealed record TestQuery(int Id)
    : IQuery<Result<string>>, IValidate
{
    public IResult Validate() =>
        Id <= 0
            ? Result.Fail<string>(Error.Validation("Id must be positive.", "Id"))
            : Result.Ok(Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Resource used in resource-based authorization tests.
/// </summary>
internal sealed record TestResource(string Id, string OwnerId);

/// <summary>
/// Command with generic resource-based authorization (<see cref="IAuthorizeResource{TResource}"/>).
/// Requires the loaded resource to check ownership.
/// </summary>
internal sealed record ResourceOwnerCommand(string ResourceId)
    : ICommand<Result<string>>, IAuthorizeResource<TestResource>
{
    public IResult Authorize(Actor actor, TestResource resource) =>
        actor.Id == resource.OwnerId
            ? Result.Ok()
            : Result.Fail(Error.Forbidden("Only the resource owner can perform this operation."));
}

/// <summary>
/// Command with both static permissions and generic resource-based authorization.
/// </summary>
internal sealed record FullAuthResourceCommand(string ResourceId)
    : ICommand<Result<string>>, IAuthorize, IAuthorizeResource<TestResource>
{
    public IReadOnlyList<string> RequiredPermissions => ["Resources.Write"];

    public IResult Authorize(Actor actor, TestResource resource) =>
        actor.Id == resource.OwnerId || actor.HasPermission("Resources.WriteAny")
            ? Result.Ok()
            : Result.Fail(Error.Forbidden("Cannot modify another user's resource."));
}

/// <summary>
/// Command combining static authorization, resource authorization, and validation.
/// </summary>
internal sealed record ValidatedFullAuthResourceCommand(string ResourceId, string Payload)
    : ICommand<Result<string>>, IAuthorize, IAuthorizeResource<TestResource>, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["Resources.Write"];

    public IResult Authorize(Actor actor, TestResource resource) =>
        actor.Id == resource.OwnerId || actor.HasPermission("Resources.WriteAny")
            ? Result.Ok()
            : Result.Fail(Error.Forbidden("Cannot modify another user's resource."));

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Payload)
            ? Result.Fail<string>(Error.Validation("Payload is required.", "Payload"))
            : Result.Ok();
}