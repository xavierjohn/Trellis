namespace Trellis.Testing.AspNetCore.Http;

using System;
using System.Globalization;
using System.Linq;

/// <summary>
/// Verifies that an <see cref="HttpFileResult"/> satisfies its attached
/// <see cref="ExpectedOutcome"/>.
/// </summary>
/// <remarks>
/// <para>Semantics:</para>
/// <list type="bullet">
/// <item><description>If <see cref="HttpFileResult.Expected"/> is <see langword="null"/>, a status-range sanity check (100-399) is applied — this catches endpoints that suddenly 500 without forcing authors to annotate every happy-path request.</description></item>
/// <item><description>If <see cref="ExpectedOutcome.StatusMin"/> / <see cref="ExpectedOutcome.StatusMax"/> are set, status must be in the inclusive range.</description></item>
/// <item><description>Every entry in <see cref="ExpectedOutcome.RequiredHeaders"/> must be present on the response with a non-empty value.</description></item>
/// </list>
/// </remarks>
public static class HttpFileAssertions
{
    /// <summary>
    /// Asserts that <paramref name="result"/> meets its expectations.
    /// Throws <see cref="HttpFileAssertionException"/> on failure.
    /// </summary>
    /// <param name="result">Per-request result returned by <see cref="HttpFileRunner"/>.</param>
    public static void AssertExpectationsMet(HttpFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var status = (int)result.Response.StatusCode;
        var title = result.Request.Title;

        if (result.Expected is null)
        {
            // Default contract: treat any non-error response as "not obviously broken".
            if (status >= 400)
            {
                throw new HttpFileAssertionException(
                    $"[{title}] expected a non-error status (default contract: 100-399), got {status}. Body: {Truncate(result.Body)}");
            }

            return;
        }

        if (result.Expected.StatusMin.HasValue && result.Expected.StatusMax.HasValue)
        {
            var min = result.Expected.StatusMin.Value;
            var max = result.Expected.StatusMax.Value;
            if (status < min || status > max)
            {
                var rangeText = min == max
                    ? min.ToString(CultureInfo.InvariantCulture)
                    : $"{min}-{max}";
                throw new HttpFileAssertionException(
                    $"[{title}] expected status {rangeText}, got {status}. Body: {Truncate(result.Body)}");
            }
        }

        if (result.Expected.RequiredHeaders.Count > 0)
        {
            foreach (var header in result.Expected.RequiredHeaders)
            {
                if (!TryGetHeaderValue(result, header, out var value) || string.IsNullOrEmpty(value))
                {
                    var names = GetAllHeaderNames(result);
                    throw new HttpFileAssertionException(
                        $"[{title}] expected header '{header}' to be present and non-empty, but was missing. Present headers: {names}");
                }
            }
        }
    }

    private static bool TryGetHeaderValue(HttpFileResult result, string name, out string? value)
    {
        if (result.Response.Headers.TryGetValues(name, out var values))
        {
            value = string.Join(", ", values);
            return true;
        }

        if (result.Response.Content is not null
            && result.Response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            value = string.Join(", ", contentValues);
            return true;
        }

        value = null;
        return false;
    }

    private static string GetAllHeaderNames(HttpFileResult result)
    {
        var names = result.Response.Headers.Select(h => h.Key);
        if (result.Response.Content is not null)
        {
            names = names.Concat(result.Response.Content.Headers.Select(h => h.Key));
        }

        return string.Join(", ", names);
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "<empty>";
        }

        return s.Length <= 200 ? s : s[..200] + "…";
    }
}
