using Trellis.Authorization;

namespace AuthorizationExample;

/// <summary>
/// Test actors with varying permissions.
/// </summary>
public static class Actors
{
    public static readonly Actor Alice = Actor.Create("alice", new HashSet<string> { "Documents.Publish" });
    public static readonly Actor Bob = Actor.Create("bob", new HashSet<string>());
    public static readonly Actor Charlie = Actor.Create("charlie", new HashSet<string> { "Documents.Publish", "Documents.Delete", "Documents.EditAny" });

    public static void PrintActors()
    {
        Console.WriteLine("Actors:");
        Console.WriteLine("  Alice   — Owner         Permissions: Documents.Publish");
        Console.WriteLine("  Bob     — Contributor   Permissions: (none)");
        Console.WriteLine("  Charlie — Admin         Permissions: Documents.Publish, Documents.Delete, Documents.EditAny");
        Console.WriteLine();
    }
}

/// <summary>
/// Simple IActorProvider for console applications.
/// In a web app, this would extract the actor from HttpContext.User.
/// </summary>
public sealed class InMemoryActorProvider(Actor initialActor) : IActorProvider
{
    public Actor CurrentActor { get; set; } = initialActor;
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentActor);
}