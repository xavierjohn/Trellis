namespace Trellis.Asp;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

/// <summary>
/// A Minimal API <see cref="IResult"/> that returns HTTP 206 Partial Content with a <c>Content-Range</c> header.
/// Used to indicate that the response contains a subset of the requested resource.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the HTTP 206 Partial Content response as defined in RFC 9110 §15.3.7.
/// It is the Minimal API equivalent of <see cref="PartialContentResult"/> (which targets MVC controllers).
/// </para>
/// <para>
/// The Content-Range header format is: <c>{unit} {from}-{to}/{total}</c>
/// <example>Content-Range: items 0-24/100</example>
/// </para>
/// <para>
/// This class is typically used by the <c>ToHttpResult</c> pagination overloads in
/// <see cref="HttpResultExtensions"/> and should rarely be instantiated directly in endpoint code.
/// </para>
/// </remarks>
public sealed class PartialContentHttpResult : IResult
{
    private readonly ContentRangeHeaderValue _contentRangeHeaderValue;
    private readonly IResult _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialContentHttpResult"/> class with explicit range values.
    /// </summary>
    /// <param name="rangeStart">The starting index of the range (zero-indexed, inclusive).</param>
    /// <param name="rangeEnd">The ending index of the range (zero-indexed, inclusive).</param>
    /// <param name="totalLength">The total number of items available, or null if unknown.</param>
    /// <param name="inner">An <see cref="IResult"/> that writes the response body (e.g., <c>Results.Ok(value)</c>).</param>
    /// <remarks>
    /// The range is inclusive on both ends: [rangeStart, rangeEnd].
    /// The Content-Range header uses "items" as the unit by default.
    /// If totalLength is null, the header is formatted as: <c>items {from}-{to}/*</c>
    /// </remarks>
    public PartialContentHttpResult(long rangeStart, long rangeEnd, long? totalLength, IResult inner)
    {
        _contentRangeHeaderValue = totalLength is null
            ? new ContentRangeHeaderValue(rangeStart, rangeEnd) { Unit = "items" }
            : new ContentRangeHeaderValue(rangeStart, rangeEnd, totalLength.Value) { Unit = "items" };
        _inner = inner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialContentHttpResult"/> class with a pre-configured <see cref="ContentRangeHeaderValue"/>.
    /// </summary>
    /// <param name="contentRangeHeaderValue">The Content-Range header value to use in the response.</param>
    /// <param name="inner">An <see cref="IResult"/> that writes the response body (e.g., <c>Results.Ok(value)</c>).</param>
    public PartialContentHttpResult(ContentRangeHeaderValue contentRangeHeaderValue, IResult inner)
    {
        _contentRangeHeaderValue = contentRangeHeaderValue;
        _inner = inner;
    }

    /// <summary>
    /// Gets the Content-Range header value that will be included in the response.
    /// </summary>
    public ContentRangeHeaderValue ContentRangeHeaderValue => _contentRangeHeaderValue;

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        // Set 206 status and Content-Range header, then delegate body writing to the inner result.
        // The inner result (e.g., Results.Ok) would normally set 200, but we override it.
        httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
        httpContext.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.ContentRange] = _contentRangeHeaderValue.ToString();
        await _inner.ExecuteAsync(httpContext).ConfigureAwait(false);
        httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
    }
}
