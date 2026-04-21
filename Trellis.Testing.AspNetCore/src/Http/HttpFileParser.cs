namespace Trellis.Testing.AspNetCore.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses a <c>.http</c> file (VS Code REST Client / Visual Studio format) into
/// a sequence of <see cref="HttpFileRequest"/> values ready to be replayed by
/// <see cref="HttpFileRunner"/>.
/// </summary>
/// <remarks>
/// <para>Supported syntax:</para>
/// <list type="bullet">
/// <item><description><c>### Title</c> separators (title becomes test-case display name).</description></item>
/// <item><description><c># @name foo</c> metadata line — binds this request's response under key <c>foo</c> for later <c>{{foo.response.*}}</c> substitution.</description></item>
/// <item><description><c># @expect status: 201</c> / <c>2xx</c> / <c>200-299</c> — optional status assertion.</description></item>
/// <item><description><c># @expect header: ETag</c> — required response header (must be present and non-empty).</description></item>
/// <item><description><c># @parity: status-only</c> — cross-host parity directive.</description></item>
/// <item><description><c>@variable = value</c> — file-level variables usable via <c>{{variable}}</c>.</description></item>
/// <item><description><c>{{named.response.body.jsonPath}}</c> — dotted-path substitution against a prior captured response body (JSON).</description></item>
/// <item><description><c>{{named.response.headers.Name}}</c> — header substitution against a prior captured response.</description></item>
/// </list>
/// <para>Expectation semantics consumed by <see cref="HttpFileAssertions"/>:
/// if no <c>@expect</c> is present on a request, the runner still asserts the
/// status falls in the non-error range 100-399. This catches regressions
/// (e.g. a happy-path endpoint suddenly 500'ing) without forcing authors to
/// annotate every 2xx request.</para>
/// </remarks>
public static class HttpFileParser
{
    private static readonly Regex StatusSingleRegex = new(@"^\s*(\d{3})\s*$", RegexOptions.Compiled);
    private static readonly Regex StatusRangeRegex = new(@"^\s*(\d{3})\s*-\s*(\d{3})\s*$", RegexOptions.Compiled);
    private static readonly Regex StatusClassRegex = new(@"^\s*([1-5])xx\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VariableLineRegex = new(@"^@([A-Za-z_][A-Za-z0-9_\-]*)\s*=\s*(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses raw <c>.http</c> file <paramref name="content"/> into an ordered
    /// list of <see cref="HttpFileRequest"/>.
    /// </summary>
    /// <param name="content">The file contents as a single string.</param>
    /// <param name="vars">Optional external variables (e.g. merged <c>$shared</c> + selected env from <c>http-client.env.json</c>). These are overridden by file-level <c>@var</c> lines.</param>
    /// <returns>An ordered, read-only list of parsed requests.</returns>
    public static IReadOnlyList<HttpFileRequest> Parse(string content, IReadOnlyDictionary<string, string>? vars = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (vars is not null)
        {
            foreach (var kv in vars)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        var results = new List<HttpFileRequest>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        // Working state for the current request.
        string? title = null;
        string? name = null;
        string? parity = null;
        int? statusMin = null;
        int? statusMax = null;
        var requiredHeaders = new List<string>();
        string? method = null;
        string? url = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        StringBuilder? body = null;
        bool inBody = false;

        void ResetCurrent()
        {
            title = null;
            name = null;
            parity = null;
            statusMin = null;
            statusMax = null;
            requiredHeaders = [];
            method = null;
            url = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            body = null;
            inBody = false;
        }

        void FlushCurrent()
        {
            if (method is null || url is null)
            {
                ResetCurrent();
                return;
            }

            var bodyText = body is null ? null : body.ToString().TrimEnd('\r', '\n');
            if (bodyText is { Length: 0 })
            {
                bodyText = null;
            }

            ExpectedOutcome? expected = null;
            if (statusMin.HasValue || requiredHeaders.Count > 0)
            {
                expected = new ExpectedOutcome(statusMin, statusMax, requiredHeaders);
            }

            // Substitute only file-level vars at parse time. Response placeholders
            // are deferred to execution time.
            var substitutedUrl = SubstituteStaticVars(url, merged);
            var substitutedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
            {
                substitutedHeaders[h.Key] = SubstituteStaticVars(h.Value, merged);
            }

            var substitutedBody = bodyText is null ? null : SubstituteStaticVars(bodyText, merged);

            results.Add(new HttpFileRequest(
                Title: title ?? $"Request {results.Count + 1}",
                Method: method,
                Url: substitutedUrl,
                Headers: substitutedHeaders,
                Body: substitutedBody,
                Name: name,
                Expected: expected,
                ParityMode: parity));

            ResetCurrent();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Body mode: capture everything verbatim until the next ### separator.
            if (inBody)
            {
                if (line.StartsWith("###", StringComparison.Ordinal))
                {
                    FlushCurrent();
                    HandleSeparator(line, ref title, ref parity);
                    continue;
                }

                body ??= new StringBuilder();
                body.AppendLine(line);
                continue;
            }

            // ### separator — starts (or continues titling) a request.
            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                // If we already collected method/url for the previous request, flush it.
                if (method is not null)
                {
                    FlushCurrent();
                }

                HandleSeparator(line, ref title, ref parity);
                continue;
            }

            var trimmed = line.TrimStart();

            // Blank line: if we have seen a request line, switch to body mode.
            if (trimmed.Length == 0)
            {
                if (method is not null)
                {
                    inBody = true;
                }

                continue;
            }

            // File-level variable: @foo = bar (only when no request is currently accumulating
            // and we're not parsing headers).
            if (method is null && line.Length > 0 && line[0] == '@')
            {
                var m = VariableLineRegex.Match(line);
                if (m.Success)
                {
                    merged[m.Groups[1].Value] = m.Groups[2].Value.Trim();
                    continue;
                }
            }

            // Comment / metadata lines.
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                // Examine for metadata pragmas.
                var after = trimmed.TrimStart('#').TrimStart();
                if (after.StartsWith("@name", StringComparison.OrdinalIgnoreCase))
                {
                    name = after.Substring("@name".Length).Trim();
                    continue;
                }

                if (after.StartsWith("@expect", StringComparison.OrdinalIgnoreCase))
                {
                    ParseExpect(after.Substring("@expect".Length).Trim(), ref statusMin, ref statusMax, requiredHeaders);
                    continue;
                }

                if (after.StartsWith("@parity", StringComparison.OrdinalIgnoreCase))
                {
                    parity = ExtractParity(after.Substring("@parity".Length).Trim());
                    continue;
                }

                // Plain comment — ignore.
                continue;
            }

            // Method line (first non-empty non-comment line after a title).
            if (method is null)
            {
                var (m, u) = ParseRequestLine(line);
                if (m is null || u is null)
                {
                    // Treat as malformed — skip.
                    continue;
                }

                method = m;
                url = u;
                continue;
            }

            // Header line.
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var hName = line[..colon].Trim();
                var hValue = line[(colon + 1)..].Trim();
                if (hName.Length > 0)
                {
                    headers[hName] = hValue;
                }
            }
        }

        // Flush the last in-flight request (if any).
        if (method is not null)
        {
            FlushCurrent();
        }

        return results;
    }

    /// <summary>
    /// Parses a <c>.http</c> file from disk.
    /// </summary>
    /// <param name="path">Path to the file to parse.</param>
    /// <param name="vars">Optional external variables merged into the file's own <c>@var</c> lines.</param>
    /// <returns>An ordered, read-only list of parsed requests.</returns>
    public static IReadOnlyList<HttpFileRequest> ParseFile(string path, IReadOnlyDictionary<string, string>? vars = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var content = File.ReadAllText(path);
        return Parse(content, vars);
    }

    /// <summary>
    /// Substitutes <c>{{var}}</c> tokens using <paramref name="vars"/>. Unknown tokens
    /// (including <c>{{name.response.*}}</c>) are left intact for later resolution by
    /// <see cref="HttpFileRunner"/>.
    /// </summary>
    /// <param name="input">Text to transform.</param>
    /// <param name="vars">Variable bag.</param>
    /// <returns>Input with matching <c>{{var}}</c> tokens replaced.</returns>
    internal static string SubstituteStaticVars(string input, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(input) || input.IndexOf("{{", StringComparison.Ordinal) < 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            if (i + 1 < input.Length && input[i] == '{' && input[i + 1] == '{')
            {
                var end = input.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    var token = input.Substring(i + 2, end - (i + 2)).Trim();
                    // Defer any token containing "." (e.g. name.response.body.x) to the runner.
                    if ((!token.Contains('.', StringComparison.Ordinal))
                        && vars.TryGetValue(token, out var value))
                    {
                        sb.Append(value);
                        i = end + 2;
                        continue;
                    }

                    // Unknown or deferred: keep verbatim.
                    sb.Append(input, i, end + 2 - i);
                    i = end + 2;
                    continue;
                }
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }

    private static void HandleSeparator(string line, ref string? title, ref string? parity)
    {
        // Take everything after leading '#' characters as the title (trimmed).
        var idx = 0;
        while (idx < line.Length && line[idx] == '#')
        {
            idx++;
        }

        var raw = line.Substring(idx).Trim();

        // Skip pure decoration lines like "══════════════" or lines that are empty.
        if (raw.Length == 0 || IsDecoration(raw))
        {
            return;
        }

        // Treat "### @parity: status-only" (or any "@<key>: value") as metadata,
        // not a title. This lets sample .http files add parity hints without
        // spawning bogus requests.
        if (raw.StartsWith('@'))
        {
            var directive = raw.TrimStart('@');
            if (directive.StartsWith("parity", StringComparison.OrdinalIgnoreCase))
            {
                parity = ExtractParity(directive.Substring("parity".Length).Trim());
            }

            return;
        }

        // If we already set a title for this request, append additional titles.
        title = title is null ? raw : title + " / " + raw;
    }

    private static bool IsDecoration(string s)
    {
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static (string? method, string? url) ParseRequestLine(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return (null, null);
        }

        return (parts[0].ToUpperInvariant(), parts[1]);
    }

    private static void ParseExpect(string payload, ref int? statusMin, ref int? statusMax, List<string> requiredHeaders)
    {
        // payload like "status: 201" or "header: ETag" or "status: 2xx"
        var colon = payload.IndexOf(':');
        if (colon < 0)
        {
            return;
        }

        var key = payload[..colon].Trim();
        var value = payload[(colon + 1)..].Trim();
        if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase))
        {
            var (min, max) = ParseStatusExpression(value);
            if (min.HasValue)
            {
                statusMin = min;
                statusMax = max;
            }
        }
        else if (string.Equals(key, "header", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length > 0)
            {
                requiredHeaders.Add(value);
            }
        }
    }

    private static (int? min, int? max) ParseStatusExpression(string value)
    {
        var m = StatusSingleRegex.Match(value);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (n, n);
        }

        m = StatusRangeRegex.Match(value);
        if (m.Success)
        {
            var a = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var b = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (a, b);
        }

        m = StatusClassRegex.Match(value);
        if (m.Success)
        {
            var c = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (c * 100, (c * 100) + 99);
        }

        return (null, null);
    }

    private static string ExtractParity(string payload)
    {
        var colon = payload.IndexOf(':');
        return colon < 0 ? payload.Trim() : payload[(colon + 1)..].Trim();
    }
}
