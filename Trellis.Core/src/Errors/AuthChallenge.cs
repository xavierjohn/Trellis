namespace Trellis;

using System.Collections.Immutable;

/// <summary>
/// Represents a single HTTP authentication challenge (RFC 9110 §11.6.1) carried by
/// <see cref="Error.Unauthorized"/>. Multiple challenges may be combined to round-trip
/// the full <c>WWW-Authenticate</c> header.
/// </summary>
/// <param name="Scheme">The auth scheme (e.g. <c>"Bearer"</c>, <c>"Basic"</c>).</param>
/// <param name="Params">
/// Optional parameters for the challenge (e.g. <c>realm</c>, <c>scope</c>, <c>error</c>).
/// Compared by value contents; parameter order is not significant.
/// </param>
public sealed record AuthChallenge(string Scheme, ImmutableDictionary<string, string>? Params = null)
{
    private static readonly StringComparer TokenComparer = StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc />
    public bool Equals(AuthChallenge? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!TokenComparer.Equals(Scheme, other.Scheme)) return false;
        return DictionaryEquals(Params, other.Params);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TokenComparer.GetHashCode(Scheme), ParamsHash(Params));

    private static bool DictionaryEquals(ImmutableDictionary<string, string>? a, ImmutableDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        var ca = a?.Count ?? 0;
        var cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;
        foreach (var kv in a!)
            if (!TryGetValueIgnoreCase(b!, kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal))
                return false;
        return true;
    }

    private static bool TryGetValueIgnoreCase(ImmutableDictionary<string, string> dictionary, string key, out string? value)
    {
        if (dictionary.TryGetValue(key, out var exactValue))
        {
            value = exactValue;
            return true;
        }

        foreach (var kv in dictionary)
        {
            if (!TokenComparer.Equals(kv.Key, key)) continue;
            value = kv.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static int ParamsHash(ImmutableDictionary<string, string>? p)
    {
        if (p is null || p.Count == 0) return 0;
        var hc = 0;
        foreach (var kv in p)
            hc ^= HashCode.Combine(TokenComparer.GetHashCode(kv.Key), kv.Value);
        return hc;
    }
}
