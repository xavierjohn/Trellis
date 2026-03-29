namespace Trellis.Asp.Authorization;

using Trellis.Authorization;

/// <summary>
/// Decorating <see cref="IActorProvider"/> that caches the result of the inner provider
/// so that multiple calls within the same scope return the same actor without
/// repeating expensive operations (e.g., database lookups).
/// </summary>
/// <remarks>
/// <para>
/// The cached value is a <see cref="Task{TResult}"/>, so concurrent calls within
/// the same scope will share the same in-flight task. Register as scoped via
/// <see cref="ServiceCollectionExtensions.AddCachingActorProvider{T}"/>.
/// </para>
/// </remarks>
public sealed class CachingActorProvider(IActorProvider inner) : IActorProvider
{
    private Task<Actor>? _cachedTask;

    /// <inheritdoc />
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        // Use CancellationToken.None for the shared resolution to avoid one caller's
        // cancellation affecting others. Caller cancellation is applied to the wait only.
        var task = LazyInitializer.EnsureInitialized(
            ref _cachedTask,
            () => inner.GetCurrentActorAsync(CancellationToken.None));

        // If the caller's token is cancelable, wrap so their cancellation doesn't
        // cancel the shared task — only their await.
        return cancellationToken.CanBeCanceled
            ? WaitWithCancellation(task!, cancellationToken)
            : task!;
    }

    private static async Task<Actor> WaitWithCancellation(Task<Actor> task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await task.WaitAsync(ct).ConfigureAwait(false);
        return result;
    }
}
