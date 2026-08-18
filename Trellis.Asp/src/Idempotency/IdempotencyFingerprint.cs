namespace Trellis.Asp.Idempotency;

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Computes a deterministic SHA-256 fingerprint over the request components that matter for
/// safe idempotent replay: method, path, canonicalized query, body bytes, <c>Content-Type</c>,
/// <c>Content-Encoding</c>, and any headers listed in
/// <see cref="IdempotencyOptions.AdditionalFingerprintHeaders"/>.
/// </summary>
/// <remarks>
/// Every component is length-framed before being fed into the hash so that distinct inputs
/// cannot serialize to the same byte sequence. For multi-valued components (query keys,
/// header values) the count is hashed alongside each value, and configured headers carry a
/// presence marker so a missing header never collides with the same header present with an
/// empty value. The on-disk format is internal; only the resulting URL-safe base64 digest
/// is part of the public contract.
/// </remarks>
public static class IdempotencyFingerprint
{
    private const byte HeaderAbsent = 0x00;
    private const byte HeaderPresent = 0x01;

    /// <summary>Computes the canonical fingerprint of a request.</summary>
    /// <param name="context">The HTTP context whose request is being fingerprinted.</param>
    /// <param name="body">The fully-buffered request body bytes (may be empty).</param>
    /// <param name="options">The idempotency options driving which headers participate.</param>
    /// <returns>A URL-safe base64 (no padding) SHA-256 digest.</returns>
    public static string Compute(HttpContext context, ReadOnlyMemory<byte> body, IdempotencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendLengthPrefixedString(sha, context.Request.Method ?? string.Empty);

        var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : string.Empty;
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
        AppendLengthPrefixedString(sha, pathBase + path);

        AppendCanonicalQuery(sha, context.Request.Query);

        AppendLengthPrefixedString(sha, context.Request.ContentType ?? string.Empty);
        AppendLengthPrefixedString(
            sha,
            context.Request.Headers.TryGetValue("Content-Encoding", out var enc) ? enc.ToString() : string.Empty);

        AppendAdditionalHeaders(sha, context.Request.Headers, options);

        AppendLengthPrefixedBytes(sha, body.Span);

        var digest = sha.GetHashAndReset();
        return ToUrlSafeBase64NoPadding(digest);
    }

    private static void AppendInt32(IncrementalHash sha, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        sha.AppendData(buf);
    }

    private static void AppendLengthPrefixedBytes(IncrementalHash sha, ReadOnlySpan<byte> value)
    {
        AppendInt32(sha, value.Length);
        sha.AppendData(value);
    }

    private static void AppendLengthPrefixedString(IncrementalHash sha, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        AppendInt32(sha, byteCount);
        if (byteCount == 0)
        {
            return;
        }

        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(value, rented);
            sha.AppendData(rented.AsSpan(0, written));
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendCanonicalQuery(IncrementalHash sha, IQueryCollection query)
    {
        var sortedKeys = query.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        AppendInt32(sha, sortedKeys.Length);
        foreach (var key in sortedKeys)
        {
            AppendLengthPrefixedString(sha, key);
            var values = query[key];
            AppendInt32(sha, values.Count);
            for (var vi = 0; vi < values.Count; vi++)
            {
                AppendLengthPrefixedString(sha, values[vi] ?? string.Empty);
            }
        }
    }

    private static void AppendAdditionalHeaders(IncrementalHash sha, IHeaderDictionary headers, IdempotencyOptions options)
    {
        var sortedNames = options.AdditionalFingerprintHeaders
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AppendInt32(sha, sortedNames.Length);
        foreach (var name in sortedNames)
        {
            AppendLengthPrefixedString(sha, name);
            if (headers.TryGetValue(name, out var values))
            {
                sha.AppendData([HeaderPresent]);
                AppendInt32(sha, values.Count);
                for (var vi = 0; vi < values.Count; vi++)
                {
                    AppendLengthPrefixedString(sha, (values[vi] ?? string.Empty).Trim());
                }
            }
            else
            {
                sha.AppendData([HeaderAbsent]);
            }
        }
    }

    private static string ToUrlSafeBase64NoPadding(byte[] digest)
    {
        var b64 = Convert.ToBase64String(digest);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}