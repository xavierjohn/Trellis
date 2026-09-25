namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Trellis;

/// <summary>
/// Configures ASP.NET Core rate-limit rejections to use the canonical Trellis Problem Details
/// response.
/// </summary>
public static class RateLimiterOptionsExtensions
{
    private const string ObserverMutationMessage =
        "The Trellis rate-limit rejection observer must not mutate or start the HTTP response.";

    /// <summary>
    /// Installs the Trellis rejection handler on <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The ASP.NET Core rate-limiter options.</param>
    /// <param name="observer">
    /// An optional logging or metrics observer invoked before Trellis writes the response.
    /// The observer may inspect the rejection context but must not mutate or start its HTTP
    /// response, including from an <c>OnStarting</c> callback.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="RateLimiterOptions.OnRejected"/> already has a handler, or
    /// <paramref name="observer"/> mutates or starts the response.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The handler converts <see cref="MetadataName.RetryAfter"/> lease metadata into
    /// <see cref="RetryAdvice"/> and writes <see cref="Error.RateLimited"/> through the normal
    /// Trellis response pipeline. Rejections therefore use the configured
    /// <see cref="TrellisAspOptions"/> mapping and emit <c>Retry-After</c> when the limiter
    /// supplies the metadata.
    /// </para>
    /// <para>
    /// This method owns <see cref="RateLimiterOptions.OnRejected"/> and does not compose with an
    /// existing handler because that handler may already write a response. Pass logging or metrics
    /// work through <paramref name="observer"/> instead. Applications that need a different writer
    /// should not install this adapter.
    /// </para>
    /// <para>
    /// Register endpoint policies by name without a policy-level <c>OnRejected</c> callback.
    /// ASP.NET Core gives policy-level callbacks precedence over the options handler; inline
    /// <c>RequireRateLimiting(policy)</c> bypasses it even when the policy callback is null.
    /// These endpoint configurations cannot be rendered by this adapter.
    /// </para>
    /// </remarks>
    public static RateLimiterOptions UseTrellisRejectionHandler(
        this RateLimiterOptions options,
        Func<OnRejectedContext, CancellationToken, ValueTask>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.OnRejected is not null)
            throw new InvalidOperationException(
                "RateLimiterOptions already has an OnRejected handler. "
                + "Pass non-writing logging or metrics work to the Trellis observer instead.");

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (observer is not null)
                await InvokeObserverAsync(observer, context, cancellationToken).ConfigureAwait(false);

            RetryAdvice? retry = context.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out TimeSpan retryAfter)
                    ? new RetryAdvice(After: retryAfter)
                    : null;

            var error = new Error.RateLimited(retry)
            {
                Code = FaultCodes.RateLimitExceeded,
            };

            await error.ToHttpResponse().ExecuteAsync(context.HttpContext).ConfigureAwait(false);
        };

        return options;
    }

    private static async ValueTask InvokeObserverAsync(
        Func<OnRejectedContext, CancellationToken, ValueTask> observer,
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (response.HasStarted)
            throw new InvalidOperationException(ObserverMutationMessage);

        var statusCode = response.StatusCode;
        var body = response.Body;
        var headers = CaptureHeaders(response.Headers);
        var callbackStatusCode = 0;
        var callbackBody = response.Body;
        Dictionary<string, string?[]>? callbackHeaders = null;

        // OnStarting runs callbacks in reverse registration order: the snapshot registered
        // after the observer runs first, then its callbacks, then this guard.
        response.OnStarting(() =>
        {
            if (callbackHeaders is not null
                && ResponseChanged(response, callbackStatusCode, callbackBody, callbackHeaders))
            {
                throw new InvalidOperationException(ObserverMutationMessage);
            }

            return Task.CompletedTask;
        });

        await observer(context, cancellationToken).ConfigureAwait(false);

        if (response.HasStarted || ResponseChanged(response, statusCode, body, headers))
            throw new InvalidOperationException(ObserverMutationMessage);

        response.OnStarting(() =>
        {
            callbackStatusCode = response.StatusCode;
            callbackBody = response.Body;
            callbackHeaders = CaptureHeaders(response.Headers);
            return Task.CompletedTask;
        });
    }

    private static bool ResponseChanged(
        HttpResponse response,
        int statusCode,
        Stream body,
        Dictionary<string, string?[]> headers) =>
        response.StatusCode != statusCode
        || !ReferenceEquals(response.Body, body)
        || !HeadersEqual(response.Headers, headers);

    private static Dictionary<string, string?[]> CaptureHeaders(IHeaderDictionary headers) =>
        headers.ToDictionary(
            static header => header.Key,
            static header => header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

    private static bool HeadersEqual(
        IHeaderDictionary current,
        Dictionary<string, string?[]> expected)
    {
        if (current.Count != expected.Count)
            return false;

        foreach (var (name, values) in expected)
        {
            if (!current.TryGetValue(name, out var currentValues)
                || !currentValues.ToArray().SequenceEqual(values, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
