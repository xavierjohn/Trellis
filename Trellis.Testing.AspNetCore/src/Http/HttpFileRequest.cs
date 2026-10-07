namespace Trellis.Testing.AspNetCore.Http;

using System.Collections.Generic;

/// <summary>
/// A single HTTP request parsed from a <c>.http</c> file
/// (VS Code REST Client / Visual Studio HTTP file format).
/// </summary>
/// <param name="Title">Display-friendly title sourced from the <c>###</c> separator line.</param>
/// <param name="Method">HTTP method, e.g. <c>GET</c>, <c>POST</c>.</param>
/// <param name="Url">Raw request URL. Deferred <c>{{named.response.*}}</c> and <c>{{$guid}}</c> placeholders are substituted at execution time.</param>
/// <param name="Headers">Request headers as a case-insensitive map. Values may contain deferred placeholders; names must be literal.</param>
/// <param name="Body">Optional request body, possibly containing deferred placeholders. <see langword="null"/> if no body was provided.</param>
/// <param name="Name">Name captured via <c># @name foo</c>; used to expose this request's response to later requests.</param>
/// <param name="Expected">Optional expectations declared via <c># @expect</c> pragmas.</param>
/// <param name="ParityMode">Optional cross-host parity directive, e.g. <c>status-only</c>.</param>
public sealed record HttpFileRequest(
    string Title,
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? Name,
    ExpectedOutcome? Expected,
    string? ParityMode = null);