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
/// This class is typically used by the <c>ToHttpResponse</c> pagination overloads and
/// should rarely be instantiated directly in endpoint code.
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
        ArgumentNullException.ThrowIfNull(inner);
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
        ArgumentNullException.ThrowIfNull(contentRangeHeaderValue);
        ArgumentNullException.ThrowIfNull(inner);
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
        // OnStarting overrides the status code set by the inner result (e.g., Ok sets 200)
        // just before headers are flushed in production (Kestrel).
        httpContext.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).StatusCode = StatusCodes.Status206PartialContent;
            return Task.CompletedTask;
        }, httpContext.Response);

        httpContext.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.ContentRange] = _contentRangeHeaderValue.ToString();
        await _inner.ExecuteAsync(httpContext).ConfigureAwait(false);

        // In test environments (DefaultHttpContext), OnStarting may not fire.
        // Override the status code directly when the response hasn't started yet.
        if (!httpContext.Response.HasStarted)
            httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
    }
}