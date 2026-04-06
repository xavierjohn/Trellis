namespace Trellis.Testing;

using Trellis.Authorization;

/// <summary>
/// Mutable <see cref="IActorProvider"/> for integration and authorization testing.
/// Stores the current actor in an <see cref="AsyncLocal{T}"/> so parallel tests using
/// overlapping <see cref="WithActor(Actor)"/> scopes never interfere with each other.
/// </summary>
/// <example>
/// <code>
/// var actorProvider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");
///
/// // Temporarily switch to a restricted user
/// await using var scope = actorProvider.WithActor("user-1", "Orders.Read");
/// var result = await mediator.Send(new CreateOrderCommand());
/// result.Should().BeFailure(); // missing Orders.Write
/// // scope disposes → actor reverts to admin
/// </code>
/// </example>
public sealed class TestActorProvider : IActorProvider
{
    private readonly Actor _defaultActor;
    private readonly AsyncLocal<Actor?> _asyncLocalActor = new();

    /// <summary>
    /// Initializes a new <see cref="TestActorProvider"/> with the specified <see cref="Actor"/>.
    /// </summary>
    /// <param name="actor">The initial (default) actor returned when no scope is active.</param>
    public TestActorProvider(Actor actor) =>
        _defaultActor = actor;

    /// <summary>
    /// Initializes a new <see cref="TestActorProvider"/> with an actor created from the specified user ID and permissions.
    /// </summary>
    /// <param name="userId">The unique identifier of the actor.</param>
    /// <param name="permissions">The permissions granted to the actor.</param>
    public TestActorProvider(string userId, params string[] permissions)
        : this(Actor.Create(userId, new HashSet<string>(permissions)))
    {
    }

    /// <inheritdoc />
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_asyncLocalActor.Value ?? _defaultActor);

    /// <summary>
    /// Temporarily replaces the current actor for the current async flow.
    /// Returns a <see cref="TestActorScope"/> that restores the previous actor on dispose.
    /// </summary>
    /// <param name="actor">The actor to use for the duration of the scope.</param>
    /// <returns>A disposable scope that restores the previous actor.</returns>
    public TestActorScope WithActor(Actor actor)
    {
        var previous = _asyncLocalActor.Value;
        _asyncLocalActor.Value = actor;
        return new TestActorScope(_asyncLocalActor, previous);
    }

    /// <summary>
    /// Temporarily replaces the current actor with one created from the specified user ID and permissions.
    /// Returns a <see cref="TestActorScope"/> that restores the previous actor on dispose.
    /// </summary>
    /// <param name="userId">The unique identifier of the actor.</param>
    /// <param name="permissions">The permissions granted to the actor.</param>
    /// <returns>A disposable scope that restores the previous actor.</returns>
    public TestActorScope WithActor(string userId, params string[] permissions) =>
        WithActor(Actor.Create(userId, new HashSet<string>(permissions)));
}