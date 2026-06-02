namespace Trellis.Asp.Authorization;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trellis.Authorization;

/// <summary>
/// Composes an HTTP-side <see cref="IActorProvider"/> with a system-actor identity used by
/// non-HTTP execution contexts (typically a <see cref="BackgroundService"/> tick).
/// </summary>
/// <remarks>
/// <para>
/// Branches on <see cref="IHttpContextAccessor.HttpContext"/> at request/tick time:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>HttpContext present</b> (HTTP request scope) — delegates to the wrapped inner
///     provider, preserving the standard claims/bearer/header-derived actor resolution and
///     the <see cref="Maybe{T}.None"/>-on-anonymous contract that produces
///     <see cref="Error.AuthenticationRequired"/> (HTTP 401).
///   </description></item>
///   <item><description>
///     <b>HttpContext null</b> (background-worker tick, hosted service, test bootstrap that
///     opened its own scope without an ambient request) — returns
///     <see cref="Maybe.From{T}"/> wrapping the configured system actor so the same
///     mediator pipeline that handles HTTP commands also accepts worker-initiated commands.
///   </description></item>
/// </list>
/// <para>
/// Register via <see cref="ServiceCollectionExtensions.AddTrellisWorkerActor(IServiceCollection, Actor)"/>
/// or the matching <c>TrellisServiceBuilder.UseWorkerActor(Actor)</c> slot. The helper takes
/// the slot currently held by an HTTP-side provider (typically registered by
/// <see cref="ServiceCollectionExtensions.AddClaimsActorProvider"/>,
/// <see cref="ServiceCollectionExtensions.AddEntraActorProvider"/>, or
/// <see cref="ServiceCollectionExtensions.AddDevelopmentActorProvider"/>) and wraps it.
/// A registration-time validator (<see cref="WorkerActorRegistrationValidator"/>) runs at
/// host start and throws if a subsequent <c>AddXxxActorProvider</c> or
/// <c>services.AddScoped&lt;IActorProvider, ...&gt;()</c> call overwrote/appended over the
/// wrapper — background workers would no longer resolve the configured system actor and
/// would receive whatever the new provider returns (typically <see cref="Maybe{T}.None"/>
/// in a tick context, surfacing as <see cref="Error.AuthenticationRequired"/> on the first
/// command).
/// </para>
/// <para>
/// Implements <see cref="IProvideActorVaryHeaders"/> by delegating to the inner provider so
/// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> continues to emit correct
/// <c>Vary</c> headers, and <see cref="IDecoratingActorProvider"/> so diagnostics that walk
/// to the underlying provider name the actual HTTP-side type rather than the worker wrapper.
/// </para>
/// <para>
/// The inner provider is materialized lazily on first access — worker ticks with a null
/// <see cref="IHttpContextAccessor.HttpContext"/> never construct the HTTP-side provider or
/// its dependencies. When the inner is created here (descriptor is
/// <see cref="ServiceDescriptor.ImplementationFactory"/> or
/// <see cref="ServiceDescriptor.ImplementationType"/>) the wrapper owns its lifetime and
/// disposes it on scope end; for singleton <see cref="ServiceDescriptor.ImplementationInstance"/>
/// registrations the wrapper does not dispose because the consumer registered the instance
/// for app-lifetime use.
/// </para>
/// </remarks>
internal sealed class WorkerComposedActorProvider : IActorProvider, IProvideActorVaryHeaders, IDecoratingActorProvider, IAsyncDisposable, IDisposable
{
    private static readonly ConcurrentDictionary<Type, byte> _loggedAsyncOnlyTypes = new();

    private readonly bool _ownsInner;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Actor _systemActor;
    private readonly Lazy<IActorProvider> _inner;
    private readonly ILogger<WorkerComposedActorProvider>? _logger;
    private bool _disposed;

    public WorkerComposedActorProvider(
        Func<IActorProvider> innerFactory,
        bool ownsInner,
        IHttpContextAccessor httpContextAccessor,
        Actor systemActor,
        ILogger<WorkerComposedActorProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(innerFactory);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(systemActor);

        _ownsInner = ownsInner;
        _httpContextAccessor = httpContextAccessor;
        _systemActor = systemActor;
        _logger = logger;
        // ExecutionAndPublication: one winning IActorProvider instance across concurrent
        // VaryByHeaders / GetCurrentActorAsync calls in the same wrapper scope, so the
        // ownership/disposal accounting matches reality.
        _inner = new Lazy<IActorProvider>(innerFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private IActorProvider Inner => _inner.Value;

    /// <inheritdoc />
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _httpContextAccessor.HttpContext is null
            ? Task.FromResult(Maybe.From(_systemActor))
            : Inner.GetCurrentActorAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the inner provider when it implements
    /// <see cref="IProvideActorVaryHeaders"/>; returns an empty collection otherwise. Empty
    /// causes <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> to throw
    /// fail-closed, preserving the existing contract for inner providers that have not
    /// opted into the vary-header capability. Materializes the inner provider on first
    /// access — only HTTP response paths read this, so worker ticks are unaffected.
    /// </remarks>
    public IReadOnlyCollection<string> VaryByHeaders =>
        Inner is IProvideActorVaryHeaders v ? v.VaryByHeaders : [];

    IActorProvider IDecoratingActorProvider.Inner => Inner;

    /// <summary>
    /// Synchronously disposes inner providers that implement <see cref="IDisposable"/>.
    /// Inner providers that ONLY implement <see cref="IAsyncDisposable"/> are SKIPPED with a
    /// throttled warning — sync-bridging async disposal can deadlock under captured sync
    /// contexts. Callers with async-only inner providers must use <see cref="DisposeAsync"/>
    /// (e.g., via 'await using' or <see cref="IAsyncDisposable.DisposeAsync"/>) for correct
    /// cleanup.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_ownsInner || !_inner.IsValueCreated) return;

        switch (_inner.Value)
        {
            case IDisposable d:
                d.Dispose();
                break;
            case IAsyncDisposable ad:
                // Inner is async-only. Bridging via .GetAwaiter().GetResult() can deadlock
                // under captured sync contexts. Skip sync dispose and emit a one-time warning
                // pointing consumers at IAsyncDisposable / DisposeAsync.
                if (_loggedAsyncOnlyTypes.TryAdd(ad.GetType(), 0))
                    LogSkippedAsyncOnlyDispose(_logger, ad.GetType());
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_ownsInner || !_inner.IsValueCreated) return;

        switch (_inner.Value)
        {
            case IAsyncDisposable ad:
                await ad.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable d:
                d.Dispose();
                break;
        }
    }

    private static readonly Action<ILogger, string, Exception?> _logSkippedAsyncOnlyDispose =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(WorkerComposedActorProvider)),
            "WorkerComposedActorProvider.Dispose was called but inner provider " +
            "of type {InnerType} only implements IAsyncDisposable. Sync disposal " +
            "was SKIPPED to avoid deadlock; use DisposeAsync or 'await using' to " +
            "reliably dispose async-only resources. Suppressing further warnings " +
            "for this type for the application lifetime.");

    private static void LogSkippedAsyncOnlyDispose(ILogger<WorkerComposedActorProvider>? logger, Type innerType)
    {
        if (logger is not null)
            _logSkippedAsyncOnlyDispose(logger, innerType.FullName ?? innerType.Name, null);
    }
}

/// <summary>
/// Startup-time validator that fails fast if the <see cref="IActorProvider"/> slot no longer
/// resolves to <see cref="WorkerComposedActorProvider"/>. Catches the lab-documented footgun
/// where a second <c>AddXxxActorProvider</c> / <c>services.AddScoped&lt;IActorProvider&gt;()</c>
/// call after <c>AddTrellisWorkerActor</c> silently overwrites the worker composition,
/// leaving background workers unable to resolve the configured system actor.
/// </summary>
/// <remarks>
/// Implements <see cref="IHostedLifecycleService"/> so the validation runs in
/// <see cref="IHostedLifecycleService.StartingAsync"/>, which the .NET host invokes for every
/// hosted service BEFORE any hosted service's <see cref="IHostedService.StartAsync"/>. This
/// guarantees the misconfiguration throws before any other <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// has a chance to call <c>ExecuteAsync</c> and dispatch a mediator command with the wrong
/// actor. Falling back to <see cref="IHostedService.StartAsync"/> would only catch the
/// overwrite after concurrent worker ticks may have already started.
/// </remarks>
internal sealed class WorkerActorRegistrationValidator(IServiceProvider rootServices) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        using var scope = rootServices.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IActorProvider>().ToList();

        if (providers.Count != 1 || providers[0] is not WorkerComposedActorProvider)
        {
            // GetRequiredService<IActorProvider>() resolves to the LAST registered descriptor
            // when there are multiple — report that one as the "active" provider rather than [0].
            var resolvedTypeName = providers.Count == 0
                ? "(none)"
                : providers[^1].GetType().Name;

            var allTypes = providers.Count == 0
                ? "(none)"
                : string.Join(", ", providers.Select(p => p.GetType().Name));

            throw new InvalidOperationException(
                $"AddTrellisWorkerActor expects the worker-composed IActorProvider to remain the active registration. " +
                $"Found {providers.Count} IActorProvider descriptor(s) [{allTypes}]; the active provider type is '{resolvedTypeName}'. " +
                "An IActorProvider registration was added or replaced AFTER AddTrellisWorkerActor — the worker " +
                "composition has been overwritten and background-worker ticks will no longer resolve the configured system actor. " +
                "Call AddTrellisWorkerActor (or UseWorkerActor on TrellisServiceBuilder) after the HTTP-side actor provider " +
                "and do not modify the IActorProvider slot afterward.");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Sentinel type registered by <see cref="ServiceCollectionExtensions.AddTrellisWorkerActor"/>
/// so the helper can detect repeated calls without invoking captured factories.
/// </summary>
internal sealed class WorkerActorRegistrationMarker;
