namespace Trellis.Testing;

/// <summary>
/// Captures the current <see cref="Trellis.Authorization.Actor"/> and restores it on dispose.
/// Created by <see cref="TestActorProvider.WithActor(Trellis.Authorization.Actor)"/>.
/// Supports both <c>await using</c> and <c>using</c> patterns.
/// </summary>
public sealed class TestActorScope : IAsyncDisposable, IDisposable
{
    private readonly AsyncLocal<TestActorProvider.ActorSelection?> _asyncLocal;
    private readonly TestActorProvider.ActorSelection? _previousSelection;
    private bool _disposed;

    internal TestActorScope(AsyncLocal<TestActorProvider.ActorSelection?> asyncLocal, TestActorProvider.ActorSelection? previousSelection)
    {
        _asyncLocal = asyncLocal;
        _previousSelection = previousSelection;
    }

    /// <summary>Restores the previous actor on the <see cref="TestActorProvider"/>.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Restores the previous actor on the <see cref="TestActorProvider"/>.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _asyncLocal.Value = _previousSelection;
    }
}