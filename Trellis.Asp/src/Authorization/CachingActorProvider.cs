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
public sealed class CachingActorProvider : IActorProvider
{
    private readonly IActorProvider _inner;
    private readonly CancellationToken _requestAborted;
    private Task<Actor>? _cachedTask;

    /// <summary>
    /// Initializes a new <see cref="CachingActorProvider"/>.
    /// </summary>
    /// <param name="inner">The inner provider to delegate to on the first call.</param>
    /// <param name="httpContextAccessor">Provides <c>HttpContext.RequestAborted</c> for request-scoped cancellation.</param>
    public CachingActorProvider(IActorProvider inner, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _requestAborted = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
    }

    /// <inheritdoc />
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        // The shared resolution uses HttpContext.RequestAborted so expensive work
        // (e.g., DB queries) is canceled when the HTTP request ends, but individual
        // callers' tokens don't cancel the shared task for other callers.
        var task = LazyInitializer.EnsureInitialized(
            ref _cachedTask,
            () => _inner.GetCurrentActorAsync(_requestAborted));

        // If the caller's token differs and is cancelable, apply it to the await only.
        return cancellationToken.CanBeCanceled && cancellationToken != _requestAborted
            ? WaitWithCancellation(task!, cancellationToken)
            : task!;
    }

    private static async Task<Actor> WaitWithCancellation(Task<Actor> task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
}