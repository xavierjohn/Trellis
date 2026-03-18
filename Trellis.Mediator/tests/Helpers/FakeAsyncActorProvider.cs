namespace Trellis.Mediator.Tests.Helpers;

using Trellis.Authorization;

/// <summary>
/// Fake <see cref="IAsyncActorProvider"/> for testing authorization behaviors with async actor resolution.
/// </summary>
internal sealed class FakeAsyncActorProvider(Actor actor) : IAsyncActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(actor);

    public static FakeAsyncActorProvider WithPermissions(string userId, params string[] permissions)
        => new(Actor.Create(userId, permissions.ToHashSet()));

    public static FakeAsyncActorProvider NoPermissions(string userId = "user-1")
        => new(Actor.Create(userId, new HashSet<string>()));
}

/// <summary>
/// Async actor provider that introduces a delay, for testing genuine async behavior.
/// </summary>
internal sealed class DelayedAsyncActorProvider(Actor actor, TimeSpan delay) : IAsyncActorProvider
{
    public async Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return actor;
    }
}

/// <summary>
/// Async actor provider that captures the <see cref="CancellationToken"/> for verification.
/// </summary>
internal sealed class TokenCapturingAsyncActorProvider(Actor actor) : IAsyncActorProvider
{
    public CancellationToken LastCancellationToken { get; private set; }

    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        LastCancellationToken = cancellationToken;
        return Task.FromResult(actor);
    }
}

/// <summary>
/// Async actor provider that returns null, for testing null guard behavior.
/// </summary>
internal sealed class NullAsyncActorProvider : IAsyncActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<Actor>(null!);
}
