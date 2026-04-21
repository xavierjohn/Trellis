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
/// </summary>
public static class HttpFileRunner
{
    /// <summary>
    /// Runs <paramref name="requests"/> against <paramref name="client"/> in order.
    /// </summary>
    /// <param name="client">Client to execute against. Typically obtained from <c>WebApplicationFactory.CreateClient()</c>.</param>
    /// <param name="requests">Parsed requests, in execution order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of per-request results.</returns>
    public static async Task<IReadOnlyList<HttpFileResult>> RunAsync(
        HttpClient client,
        IReadOnlyList<HttpFileRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requests);

        var context = new ScenarioContext();
        var results = new List<HttpFileResult>(requests.Count);
        foreach (var req in requests)
        {
            var result = await RunSingleAsync(client, req, context, ct).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Runs a single <paramref name="request"/>, substituting any deferred
    /// <c>{{name.response.*}}</c> tokens against <paramref name="context"/>
    /// first, and recording the response into the context if the request was
    /// declared with <c># @name</c>.
    /// </summary>
    /// <param name="client">Client to execute against.</param>
    /// <param name="request">Request to run.</param>
    /// <param name="context">Shared scenario context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="HttpFileResult"/>.</returns>
    public static async Task<HttpFileResult> RunSingleAsync(
        HttpClient client,
        HttpFileRequest request,
        ScenarioContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var url = Substitute(request.Url, context);
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), BuildUri(client, url));

        HttpContent? content = null;
        if (!string.IsNullOrEmpty(request.Body))
        {
            var bodyText = Substitute(request.Body, context);
            content = new StringContent(bodyText, Encoding.UTF8);
            // Content-Type may be set from headers below; default if absent.
            content.Headers.ContentType = null;
            httpRequest.Content = content;
        }

        foreach (var header in request.Headers)
        {
            var value = Substitute(header.Value, context);
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

    private static Uri BuildUri(HttpClient client, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
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
    /// Replaces deferred <c>{{...}}</c> tokens using the scenario context.
    /// Tokens not resolvable are left intact.
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
