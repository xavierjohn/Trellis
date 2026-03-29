namespace Trellis.Mediator.Tests.Helpers;

using Trellis.Authorization;

/// <summary>
/// Fake <see cref="IActorProvider"/> for testing authorization behaviors.
/// </summary>
internal sealed class FakeActorProvider(Actor actor) : IActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(actor);

    public static FakeActorProvider WithPermissions(string userId, params string[] permissions)
        => new(Actor.Create(userId, permissions.ToHashSet()));

    public static FakeActorProvider NoPermissions(string userId = "user-1")
        => new(Actor.Create(userId, new HashSet<string>()));
}