namespace Trellis.Testing.AspNetCore.Http;

using System.Collections.Generic;

/// <summary>
/// Expected outcome parsed from <c># @expect</c> pragmas in a <c>.http</c> file.
/// </summary>
/// <param name="StatusMin">Inclusive lower bound of acceptable HTTP status codes, or <see langword="null"/> if no status expectation was declared.</param>
/// <param name="StatusMax">Inclusive upper bound of acceptable HTTP status codes, or <see langword="null"/> if no status expectation was declared.</param>
/// <param name="RequiredHeaders">Headers that must be present and non-empty on the response.</param>
/// <param name="ContentType">
/// Media type the response must carry, or <see langword="null"/> if no content-type expectation was
/// declared. Compared by media type alone, so parameters such as <c>charset</c> are ignored on both
/// sides. Declared via <c># @expect content-type: application/problem+json</c>.
/// </param>
public sealed record ExpectedOutcome(
    int? StatusMin,
    int? StatusMax,
    IReadOnlyList<string> RequiredHeaders,
    string? ContentType = null);