namespace Trellis.Testing.AspNetCore.Http;

using System.Net.Http;

/// <summary>
/// Result of replaying a single <see cref="HttpFileRequest"/>.
/// </summary>
/// <param name="Request">The request that was executed (post-substitution).</param>
/// <param name="Response">The raw response. The caller owns disposal.</param>
/// <param name="Body">Response body read as text. <see langword="null"/> if empty.</param>
/// <param name="Expected">The expectations attached to <paramref name="Request"/>, if any.</param>
public sealed record HttpFileResult(
    HttpFileRequest Request,
    HttpResponseMessage Response,
    string? Body,
    ExpectedOutcome? Expected);