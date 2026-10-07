namespace Trellis.Testing.AspNetCore.Http;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Executes a parsed <c>.http</c> file against an <see cref="HttpClient"/>,
/// threading a <see cref="ScenarioContext"/> through the requests so named
/// responses can be referenced via <c>{{name.response.body.*}}</c> substitutions.
/// Generates a fresh GUID for each <c>{{$guid}}</c> occurrence at execution time.
/// </summary>
public static class HttpFileRunner
{
    /// <summary>
    /// Runs <paramref name="requests"/> against <paramref name="client"/> in order.
    /// </summary>
    /// <param name="client">Client to execute against. Typically obtained from <c>WebApplicationFactory.CreateClient()</c>.</param>
    /// <param name="requests">Parsed requests, in execution order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of per-request results. The caller owns their responses.</returns>
    /// <exception cref="HttpFileAssertionException">A request contains an unresolved or unterminated placeholder.</exception>
    public static async Task<IReadOnlyList<HttpFileResult>> RunAsync(
        HttpClient client,
        IReadOnlyList<HttpFileRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requests);

        var context = new ScenarioContext();
        var results = new List<HttpFileResult>(requests.Count);
        var completed = false;
        try
        {
            foreach (var req in requests)
            {
                var result = await RunSingleAsync(client, req, context, ct).ConfigureAwait(false);
                results.Add(result);
            }

            completed = true;
            return results;
        }
        finally
        {
            if (!completed)
                foreach (var result in results)
                    result.Response.Dispose();
        }
    }

    /// <summary>
    /// Runs a single <paramref name="request"/>, substituting any deferred
    /// <c>{{name.response.*}}</c> tokens against <paramref name="context"/> and
    /// generating a fresh GUID per <c>{{$guid}}</c> occurrence before sending.
    /// Records the response into the context if the request was
    /// declared with <c># @name</c>.
    /// </summary>
    /// <param name="client">Client to execute against.</param>
    /// <param name="request">Request to run.</param>
    /// <param name="context">Shared scenario context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="HttpFileResult"/>.</returns>
    /// <exception cref="HttpFileAssertionException">A request contains an unresolved or unterminated placeholder.</exception>
    public static async Task<HttpFileResult> RunSingleAsync(
        HttpClient client,
        HttpFileRequest request,
        ScenarioContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var url = Resolve(request.Url, context, request, "URL");
        var bodyText = Resolve(request.Body, context, request, "body");
        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), BuildUri(client, url));

        HttpContent? content = null;
        if (!string.IsNullOrEmpty(request.Body))
        {
            content = new StringContent(bodyText, Encoding.UTF8);
            // Content-Type may be set from headers below; default if absent.
            content.Headers.ContentType = null;
            httpRequest.Content = content;
        }

        foreach (var header in request.Headers)
        {
            AssertResolved(header.Key, request, "header name");
            var value = Resolve(header.Value, context, request, $"header '{header.Key}'");
            if (IsContentHeader(header.Key))
            {
                content ??= httpRequest.Content ?? new StringContent(string.Empty, Encoding.UTF8);
                if (httpRequest.Content is null)
                {
                    httpRequest.Content = content;
                }

                content.Headers.Remove(header.Key);
                content.Headers.TryAddWithoutValidation(header.Key, value);
            }
            else
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, value);
            }
        }

        var response = await client.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var bodyStr = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(bodyStr))
        {
            bodyStr = null;
        }

        if (!string.IsNullOrEmpty(request.Name))
        {
            context.Record(request.Name, (int)response.StatusCode, SnapshotHeaders(response), bodyStr);
        }

        return new HttpFileResult(request, response, bodyStr, request.Expected);
    }

    private static string Resolve(string? input, ScenarioContext context, HttpFileRequest request, string location)
    {
        var resolved = Substitute(input, context);
        AssertResolved(resolved, request, location);
        return resolved;
    }

    private static void AssertResolved(string input, HttpFileRequest request, string location)
    {
        var start = input.IndexOf("{{", StringComparison.Ordinal);
        if (start < 0)
            return;

        var end = input.IndexOf("}}", start + 2, StringComparison.Ordinal);
        var problem = end < 0
            ? "Unterminated placeholder '{{'"
            : $"Unresolved placeholder '{input.Substring(start, end + 2 - start)}'";
        throw new HttpFileAssertionException($"{problem} in request '{request.Title}' ({location}).");
    }

    private static Uri BuildUri(HttpClient client, string url)
    {
        // Only attempt absolute parsing when the URL carries an explicit scheme.
        // On Unix, Uri.TryCreate(absolute) accepts leading-slash paths as file://
        // URIs (because '/' is a valid absolute filesystem path), which would
        // bypass HttpClient.BaseAddress and percent-encode '?' into the path —
        // breaking query strings on Linux while quietly working on Windows.
        if (url.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            return abs;
        }

        if (url.StartsWith('/'))
        {
            return new Uri(url, UriKind.Relative);
        }

        // Unanchored relative — trust HttpClient.BaseAddress to resolve it.
        return new Uri("/" + url, UriKind.Relative);
    }

    private static bool IsContentHeader(string name) =>
        name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> SnapshotHeaders(HttpResponseMessage response)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
        {
            dict[h.Key] = string.Join(", ", h.Value);
        }

        if (response.Content is not null)
        {
            foreach (var h in response.Content.Headers)
            {
                dict[h.Key] = string.Join(", ", h.Value);
            }
        }

        return dict;
    }

    /// <summary>
    /// Replaces deferred response tokens and generates a GUID per dynamic occurrence.
    /// Leaves unknown tokens intact for request-aware validation.
    /// </summary>
    internal static string Substitute(string? input, ScenarioContext context)
    {
        if (string.IsNullOrEmpty(input) || input.IndexOf("{{", StringComparison.Ordinal) < 0)
        {
            return input ?? string.Empty;
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
                    if (string.Equals(token, "$guid", StringComparison.Ordinal))
                    {
                        sb.Append(Guid.NewGuid().ToString("D"));
                        i = end + 2;
                        continue;
                    }

                    if (context.TryResolve(token, out var value))
                    {
                        sb.Append(value);
                        i = end + 2;
                        continue;
                    }

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
}