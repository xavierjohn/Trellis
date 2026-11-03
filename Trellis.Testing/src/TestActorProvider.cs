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
public sealed class TestActorProvider : IActorProvider, IActorIdentityProvider
{
    private readonly Actor? _defaultActor;
    private readonly AsyncLocal<ActorSelection?> _asyncLocalSelection = new();

    /// <summary>
    /// Initializes a new <see cref="TestActorProvider"/> with the specified <see cref="Actor"/>.
    /// </summary>
    /// <param name="actor">The initial (default) actor returned when no scope is active.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is <see langword="null"/>.</exception>
    public TestActorProvider(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        _defaultActor = actor;
    }

    private TestActorProvider()
    {
    }

    /// <summary>
    /// Creates a provider that resolves no actor and no identity until a
    /// <see cref="WithActor(Actor)"/> scope is opened, for testing the unauthenticated path.
    /// </summary>
    /// <returns>An anonymous provider.</returns>
    public static TestActorProvider Anonymous() => new();

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
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current() is { } actor ? Maybe.From(actor) : Maybe<Actor>.None);

    /// <inheritdoc />
    public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Current() is { } actor ? Maybe.From(actor.Id) : Maybe<ActorId>.None);
    }

    /// <summary>
    /// Temporarily replaces the current actor for the current async flow.
    /// Returns a <see cref="TestActorScope"/> that restores the previous actor on dispose.
    /// </summary>
    /// <param name="actor">The actor to use for the duration of the scope.</param>
    /// <returns>A disposable scope that restores the previous actor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is <see langword="null"/>.</exception>
    public TestActorScope WithActor(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Select(new ActorSelection(actor));
    }

    /// <summary>
    /// Temporarily makes the current async flow anonymous: no actor and no identity resolve.
    /// Returns a <see cref="TestActorScope"/> that restores the previous actor on dispose.
    /// </summary>
    /// <returns>A disposable scope that restores the previous actor.</returns>
    public TestActorScope WithoutActor() => Select(new ActorSelection(null));

    /// <summary>
    /// Temporarily replaces the current actor with one created from the specified user ID and permissions.
    /// Returns a <see cref="TestActorScope"/> that restores the previous actor on dispose.
    /// </summary>
    /// <param name="userId">The unique identifier of the actor.</param>
    /// <param name="permissions">The permissions granted to the actor.</param>
    /// <returns>A disposable scope that restores the previous actor.</returns>
    public TestActorScope WithActor(string userId, params string[] permissions) =>
        WithActor(Actor.Create(userId, new HashSet<string>(permissions)));

    private Actor? Current() => _asyncLocalSelection.Value is { } selection ? selection.Actor : _defaultActor;

    private TestActorScope Select(ActorSelection selection)
    {
        var previous = _asyncLocalSelection.Value;
        _asyncLocalSelection.Value = selection;
        return new TestActorScope(_asyncLocalSelection, previous);
    }

    internal sealed record ActorSelection(Actor? Actor);
}