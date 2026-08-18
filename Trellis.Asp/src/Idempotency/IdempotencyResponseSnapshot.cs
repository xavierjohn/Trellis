namespace Trellis.Asp.Idempotency;

using System;
using System.Collections.Generic;

/// <summary>
/// Captured representation of a previously emitted response, replayed verbatim when a retry
/// matches the original request fingerprint.
/// </summary>
/// <param name="StatusCode">The HTTP status code of the original response.</param>
/// <param name="Headers">
/// Response headers as written by the handler, after <see cref="IdempotencyOptions"/> filtering
/// (for example <c>Set-Cookie</c> stripped when <see cref="IdempotencyOptions.IncludeSetCookieInSnapshot"/>
/// is <c>false</c>). Header names are matched case-insensitively.
/// </param>
/// <param name="Body">The raw response body bytes captured from the handler.</param>
/// <param name="Fingerprint">
/// The request fingerprint that produced this snapshot. Stored alongside the response so a
/// replay can reject a key reused with a different body.
/// </param>
/// <remarks>
/// Record equality on this type is the C# record default: scalar fields (<see cref="StatusCode"/>,
/// <see cref="Fingerprint"/>) compare by value, but <see cref="Headers"/> and <see cref="Body"/>
/// compare by <em>reference</em> because <see cref="EqualityComparer{T}.Default"/> for
/// <see cref="IReadOnlyDictionary{TKey, TValue}"/> and <see cref="byte"/>[] falls back to
/// <see cref="object.ReferenceEquals(object, object)"/>. Snapshot equality is not used by the
/// middleware or the in-memory store for lookups (those are keyed by <c>(scope, key)</c>);
/// consumers that need to compare two snapshots structurally must do so explicitly.
/// </remarks>
public sealed record IdempotencyResponseSnapshot(
    int StatusCode,
    IReadOnlyDictionary<string, string[]> Headers,
    byte[] Body,
    string Fingerprint);