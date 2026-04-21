namespace Trellis.Testing.AspNetCore.Http;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Mutable state shared across a sequence of HTTP file requests. Captures each
/// <c># @name foo</c>-annotated response so later requests can reference
/// <c>{{foo.response.body.x}}</c> / <c>{{foo.response.headers.Name}}</c>.
/// </summary>
public sealed class ScenarioContext
{
    private readonly Dictionary<string, NamedResponse> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a named response for later substitution.</summary>
    /// <param name="name">The name assigned via <c># @name</c>.</param>
    /// <param name="status">HTTP status code.</param>
    /// <param name="headers">Response headers captured as plain name/value pairs.</param>
    /// <param name="body">Raw response body as text, or <see langword="null"/> if none.</param>
    public void Record(string name, int status, IReadOnlyDictionary<string, string> headers, string? body)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                // Not JSON — substitution can still use headers and raw body.
                doc = null;
            }
        }

        _named[name] = new NamedResponse(status, headers, body, doc);
    }

    /// <summary>
    /// Attempts to resolve a deferred token such as
    /// <c>createOptional.response.body.id</c> or
    /// <c>createOptional.response.headers.ETag</c>.
    /// </summary>
    /// <param name="token">Token content (without surrounding <c>{{ }}</c>).</param>
    /// <param name="value">Resolved value, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="token"/> was resolved.</returns>
    public bool TryResolve(string token, out string? value)
    {
        value = null;
        var parts = token.Split('.');
        if (parts.Length < 3)
        {
            return false;
        }

        if (!_named.TryGetValue(parts[0], out var response))
        {
            return false;
        }

        if (!string.Equals(parts[1], "response", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var kind = parts[2];
        if (string.Equals(kind, "headers", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4)
            {
                return false;
            }

            var headerName = string.Join('.', parts, 3, parts.Length - 3);
            if (response.Headers.TryGetValue(headerName, out var h))
            {
                value = h;
                return true;
            }

            // Case-insensitive fallback.
            foreach (var kv in response.Headers)
            {
                if (string.Equals(kv.Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    return true;
                }
            }

            return false;
        }

        if (string.Equals(kind, "body", StringComparison.OrdinalIgnoreCase))
        {
            if (response.Body is null)
            {
                return false;
            }

            if (parts.Length == 3)
            {
                value = response.Body;
                return true;
            }

            if (response.Json is null)
            {
                return false;
            }

            var current = response.Json.RootElement;
            for (int i = 3; i < parts.Length; i++)
            {
                if (current.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetPropertyCaseInsensitive(current, parts[i], out current))
                {
                    return false;
                }
            }

            value = JsonElementToString(current);
            return true;
        }

        if (string.Equals(kind, "status", StringComparison.OrdinalIgnoreCase))
        {
            value = response.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement result)
    {
        if (obj.TryGetProperty(name, out result))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                result = prop.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static string JsonElementToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? string.Empty,
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => e.GetRawText(),
    };

    private sealed record NamedResponse(
        int Status,
        IReadOnlyDictionary<string, string> Headers,
        string? Body,
        JsonDocument? Json);
}
