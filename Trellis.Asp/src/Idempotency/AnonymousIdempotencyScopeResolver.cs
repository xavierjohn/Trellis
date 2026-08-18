namespace Trellis.Asp.Idempotency;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Default <see cref="IIdempotencyScopeResolver"/> that returns the constant sentinel
/// <c>"anonymous"</c> for every request. Safe only when the idempotency-key space is already
/// isolated per client by some other mechanism (for example an API-gateway that issues a
/// per-tenant API key); otherwise two authenticated users sending the same key value can
/// replay each other's responses.
/// </summary>
public sealed class AnonymousIdempotencyScopeResolver : IIdempotencyScopeResolver
{
    /// <summary>Sentinel value returned for every request.</summary>
    public const string AnonymousScope = "anonymous";

    /// <inheritdoc/>
    public ValueTask<string> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AnonymousScope);
}