namespace Trellis.Testing;

/// <summary>
/// Captures the current <see cref="Trellis.Authorization.Actor"/> and restores it on dispose.
/// Created by <see cref="TestActorProvider.WithActor(Trellis.Authorization.Actor)"/>.
/// Supports both <c>await using</c> and <c>using</c> patterns.
/// </summary>
public sealed class TestActorScope : IAsyncDisposable, IDisposable
{
    private readonly AsyncLocal<Authorization.Actor?> _asyncLocal;
    private readonly Authorization.Actor? _previousActor;
    private bool _disposed;

    internal TestActorScope(AsyncLocal<Authorization.Actor?> asyncLocal, Authorization.Actor? previousActor)
    {
        _asyncLocal = asyncLocal;
        _previousActor = previousActor;
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
        _asyncLocal.Value = _previousActor;
    }
}