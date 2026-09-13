namespace Trellis.Asp.ApiVersioning;

using System;
using System.Collections.Concurrent;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Asp;

/// <summary>
/// Mid-migration safety net for <c>.WithVersionedRoute()</c> resolver chains. Emits a
/// single warning (or, when opted in, throws) when the resolver runs against an endpoint
/// that has no <see cref="ApiVersionMetadata"/> attached — the canonical "host removed
/// <c>AddApiVersioning(...)</c> but left a <c>.WithVersionedRoute()</c> chain in place"
/// regression. De-duplicated per <c>(endpoint, AppDomain)</c> pair so steady-state traffic
/// produces at most one log line per offending endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Only fires for the missing-metadata case. Neutral targets and versioned URL-segment
/// targets remain silent.
/// </para>
/// <para>
/// The de-duplication key identifies the resolved Location destination, not the endpoint
/// handling the request. Several source actions linking to the same unversioned target
/// therefore produce one warning.
/// </para>
/// </remarks>
internal static partial class SilentVersionInjectionDiagnostic
{
    /// <summary>
    /// <c>ILogger</c> category name used when emitting the warning. Centralised so the
    /// cookbook recipe and integration tests can reference the exact category string.
    /// </summary>
    internal const string LoggerCategory = "Trellis.Asp.ApiVersioning";

    /// <summary>
    /// Per-process de-duplication set. <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>
    /// is atomic so at most one thread sees <c>true</c> for any given endpoint key, which
    /// means at most one warning is emitted per <c>(endpoint, AppDomain)</c> pair regardless
    /// of concurrent request volume.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> s_seenEndpoints = new();

    /// <summary>
    /// Inspects <paramref name="endpoint"/> for missing <see cref="ApiVersionMetadata"/>
    /// and, when appropriate, throws (opt-in fail-fast) or logs a single warning.
    /// Returns silently in every other case (metadata present, no endpoint,
    /// or already-warned endpoint).
    /// </summary>
    /// <param name="httpContext">
    /// The current request context. Used to resolve <see cref="IOptions{TrellisAspOptions}"/>
    /// for the fail-fast opt-in and <see cref="ILoggerFactory"/> for warning emission.
    /// </param>
    /// <param name="endpoint">
    /// The resolved Location destination. A null endpoint is ignored; the caller owns
    /// reporting unresolved destinations.
    /// </param>
    internal static void EmitIfMetadataMissing(HttpContext httpContext, Endpoint? endpoint)
    {
        if (endpoint is null)
            return;

        // Metadata present means the host did call AddApiVersioning(...) and the endpoint
        // is part of its surface — no diagnostic needed. Other ShouldSkipInjection cases
        // ([ApiVersionNeutral], URL-segment versioning) carry metadata, so this scopes the diagnostic
        // to exactly the missing-metadata case the issue describes.
        var metadata = endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
        if (metadata is not null)
            return;

        var endpointKey = endpoint.DisplayName
            ?? (endpoint as RouteEndpoint)?.RoutePattern?.RawText
            ?? "(unrouted endpoint)";

        var options = httpContext.RequestServices.GetService<IOptions<TrellisAspOptions>>()?.Value;
        var failFast = options?.FailFastOnSilentVersionInjection ?? false;

        // Fail-fast bypasses de-duplication so every offending request surfaces the bug.
        // De-duping here would cause "fail once, then succeed silently" — the opposite of
        // what an opt-in fail-fast switch is for.
        if (failFast)
            throw new InvalidOperationException(BuildMessage(endpointKey));

        // Resolve the logger BEFORE de-duplication: if ILoggerFactory is unregistered
        // (minimal test harnesses) we want a later request that does have a factory to
        // still observe the warning, not be permanently suppressed by an earlier add to
        // the seen-set that never actually emitted a log line.
        var loggerFactory = httpContext.RequestServices.GetService<ILoggerFactory>();
        if (loggerFactory is null)
            return;

        // ConcurrentDictionary.TryAdd is atomic; only one thread observes `true` per key,
        // so at most one warning is emitted per (endpoint, AppDomain) under any concurrency.
        if (!s_seenEndpoints.TryAdd(endpointKey, 0))
            return;

        LogSilentVersionInjection(loggerFactory.CreateLogger(LoggerCategory), endpointKey);
    }

    /// <summary>
    /// Clears the de-duplication set. Test-only hook so individual test cases can
    /// observe the first-skip warning in isolation; production code must never call this.
    /// </summary>
    internal static void ResetForTests() => s_seenEndpoints.Clear();

    /// <summary>
    /// Suffix shared by the warning log template and the fail-fast exception message;
    /// the only diverging fragment is the endpoint-name placeholder ("{EndpointKey}" for
    /// the source-generated logger, "'{endpointKey}'" for the interpolated exception
    /// message). Keeping the suffix in a single constant means an edit to the wording
    /// can't accidentally drift between the two paths.
    /// </summary>
    private const string MessageSuffix =
        "' but the endpoint has no ApiVersionMetadata attached — the host did not call " +
        "services.AddApiVersioning(...), or this endpoint sits outside its surface. The " +
        "api-version route value is being silently omitted from the response Location/route " +
        "URL. If this host is intentionally unversioned, remove the .WithVersionedRoute() " +
        "chain. To fail fast on this configuration instead of logging once per endpoint, " +
        "configure TrellisAspOptions.FailFastOnSilentVersionInjection = true.";

    private static string BuildMessage(string endpointKey) =>
        ".WithVersionedRoute() ran for endpoint '" + endpointKey + MessageSuffix;

    [LoggerMessage(
        EventId = 1,
        EventName = "SilentVersionInjectionSkip",
        Level = LogLevel.Warning,
        Message = ".WithVersionedRoute() ran for endpoint '{EndpointKey}" + MessageSuffix)]
    private static partial void LogSilentVersionInjection(ILogger logger, string endpointKey);
}