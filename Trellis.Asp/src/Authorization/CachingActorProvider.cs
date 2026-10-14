namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using Trellis.Authorization;

/// <summary>
/// Decorating <see cref="IActorProvider"/> that caches the result of the inner provider
/// so that multiple calls within the same scope return the same actor without
/// repeating expensive operations (e.g., database lookups).
/// </summary>
/// <remarks>
/// <para>
/// Identity-only resolution has its own lazy task and never resolves permissions through
/// the full actor method. The identity capability must be supported by the inner provider;
/// otherwise identity resolution throws a configuration error.
/// </para>
/// <para>
/// The cached value is a <see cref="Task{TResult}"/>, so concurrent calls within
/// the same scope will share the same in-flight task. Register as scoped via
/// <see cref="ServiceCollectionExtensions.AddCachingActorProvider{T}"/>.
/// </para>
/// <para>
/// <b>Failure caching.</b> If the inner provider throws or its task faults, the failure is
/// cached for the remainder of the request scope; subsequent calls re-throw the same exception
/// rather than retrying the inner provider. This avoids repeating expensive lookups (e.g.
/// database round-trips) that have already failed within the current request.
/// </para>
/// </remarks>
public sealed class CachingActorProvider : IActorProvider, IActorIdentityProvider, IProvideActorVaryHeaders, IDecoratingActorProvider
{
    private readonly IActorProvider _inner;
    private readonly CancellationToken _requestAborted;
    private readonly Lazy<Task<Maybe<Actor>>> _cachedTask;
    private readonly Lazy<Task<Maybe<ActorId>>> _cachedIdentityTask;

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the inner provider's headers when it implements
    /// <see cref="IProvideActorVaryHeaders"/>; returns an empty collection when the wrapped
    /// provider does not. An empty collection causes
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> to throw fail-closed,
    /// preventing silent cache-poisoning for custom wrapped providers that have not opted
    /// into the contract. The exception message names the inner provider (via
    /// <see cref="IDecoratingActorProvider.Inner"/>) rather than the caching wrapper.
    /// </remarks>
    public IReadOnlyCollection<string> VaryByHeaders =>
        _inner is IProvideActorVaryHeaders v ? v.VaryByHeaders : [];

    /// <summary>The inner provider this caching wrapper delegates to. Internal — exposed
    /// so the response writer can name the underlying provider in fail-closed diagnostics.</summary>
    IActorProvider IDecoratingActorProvider.Inner => _inner;

    /// <summary>
    /// Initializes a new <see cref="CachingActorProvider"/>.
    /// </summary>
    /// <param name="inner">The inner provider to delegate to on the first call.</param>
    /// <param name="httpContextAccessor">Provides <c>HttpContext.RequestAborted</c> for request-scoped cancellation.</param>
    public CachingActorProvider(IActorProvider inner, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _requestAborted = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        _cachedTask = new(() => CaptureResolution(() => _inner.GetCurrentActorAsync(_requestAborted)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _cachedIdentityTask = new(() => CaptureResolution(() =>
            ActorIdentityProviderResolver.Resolve(_inner).GetCurrentActorIdAsync(_requestAborted).AsTask()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
        ForCaller(_cachedTask.Value, cancellationToken);

    /// <summary>
    /// Resolves and caches identity independently of full actor resolution. Throws an actionable
    /// configuration error when the inner provider does not support identity-only resolution.
    /// </summary>
    /// <param name="cancellationToken">Cancels this caller's await, not other callers' shared work.</param>
    /// <returns>The request-cached canonical identity, without loading the full actor.</returns>
    public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default) =>
        new(ForCaller(_cachedIdentityTask.Value, cancellationToken));

    private Task<Maybe<T>> ForCaller<T>(Task<Maybe<T>> task, CancellationToken token) where T : notnull =>
        token.CanBeCanceled && token != _requestAborted ? WaitWithCancellation(task, token) : task;

    private static Task<Maybe<T>> CaptureResolution<T>(Func<Task<Maybe<T>>> resolve) where T : notnull
    {
        try
        {
            return resolve();
        }
        catch (Exception ex)
        {
            return Task.FromException<Maybe<T>>(ex);
        }
    }

    private static async Task<Maybe<T>> WaitWithCancellation<T>(Task<Maybe<T>> task, CancellationToken ct) where T : notnull
    {
        ct.ThrowIfCancellationRequested();
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
}