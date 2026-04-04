namespace Trellis.Asp;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// Represents an <see cref="ObjectResult"/> that returns HTTP 206 Partial Content with a Content-Range header.
/// Used to indicate that the response contains a subset of the requested resource.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the HTTP 206 Partial Content response as defined in RFC 7233.
/// It's commonly used for:
/// <list type="bullet">
/// <item>Paginated API responses</item>
/// <item>Range requests for large datasets</item>
/// <item>Incremental data loading in client applications</item>
/// <item>Resumable downloads or data transfers</item>
/// </list>
/// </para>
/// <para>
/// The Content-Range header format is: <c>{unit} {from}-{to}/{total}</c>
/// <example>Content-Range: items 0-24/100</example>
/// </para>
/// <para>
/// This class is typically used by <see cref="ActionResultExtensions"/> partial content overloads
/// and should rarely be instantiated directly in controller code.
/// </para>
/// </remarks>
/// <example>
/// Using PartialContentResult directly in a controller:
/// <code>
/// [HttpGet]
/// public IActionResult GetUsers([FromQuery] int page = 0, [FromQuery] int pageSize = 25)
/// {
///     var from = page * pageSize;
///     var to = from + pageSize - 1;
///     var totalCount = _userRepository.Count();
///     var users = _userRepository.GetRange(from, pageSize);
///     
///     if (to >= totalCount - 1)
///     {
///         // Complete result - return 200 OK
///         return Ok(users);
///     }
///     
///     // Partial result - return 206 Partial Content
///     return new PartialContentResult(from, to, totalCount, users);
/// }
/// 
/// // Response headers:
/// // HTTP/1.1 206 Partial Content
/// // Content-Range: items 0-24/100
/// </code>
/// </example>
/// <example>
/// Using with ActionResultExtensions (recommended approach):
/// <code>
/// [HttpGet]
/// public ActionResult&lt;IEnumerable&lt;UserDto&gt;&gt; GetUsers(
///     [FromQuery] int page = 0,
///     [FromQuery] int pageSize = 25)
/// {
///     return _userService
///         .GetPagedUsers(page, pageSize)
///         .ToActionResult(
///             this,
///             funcRange: r => new ContentRangeHeaderValue(r.From, r.To, r.TotalCount) { Unit = "items" },
///             funcValue: r => r.Users.Select(u => new UserDto(u)));
///     
///     // Automatically returns PartialContentResult when appropriate
/// }
/// </code>
/// </example>
public class PartialContentResult : ObjectResult
{
    private readonly ContentRangeHeaderValue _contentRangeHeaderValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialContentResult"/> class with explicit range values.
    /// </summary>
    /// <param name="rangeStart">The starting index of the range (zero-indexed, inclusive).</param>
    /// <param name="rangeEnd">The ending index of the range (zero-indexed, inclusive).</param>
    /// <param name="totalLength">The total number of items available, or null if unknown.</param>
    /// <param name="value">The partial data to include in the response body.</param>
    /// <remarks>
    /// <para>
    /// The range is inclusive on both ends: [rangeStart, rangeEnd].
    /// For example, to return items 0-24 of 100: rangeStart=0, rangeEnd=24, totalLength=100.
    /// </para>
    /// <para>
    /// The Content-Range header will be set to: <c>items {rangeStart}-{rangeEnd}/{totalLength}</c>
    /// If totalLength is null, it will be formatted as: <c>items {rangeStart}-{rangeEnd}/*</c>
    /// </para>
    /// <para>
    /// The unit "items" is used by default and is suitable for most paginated API responses.
    /// For byte-range requests (e.g., file downloads), use the constructor that accepts
    /// <see cref="ContentRangeHeaderValue"/> directly.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Return items 0-24 out of 100 total
    /// return new PartialContentResult(0, 24, 100, users);
    /// // Content-Range: items 0-24/100
    /// 
    /// // Return items 25-49, total unknown
    /// return new PartialContentResult(25, 49, null, users);
    /// // Content-Range: items 25-49/*
    /// </code>
    /// </example>
    public PartialContentResult(long rangeStart, long rangeEnd, long? totalLength, object? value)
        : base(value)
    {
        _contentRangeHeaderValue = totalLength == null
            ? new ContentRangeHeaderValue(rangeStart, rangeEnd) { Unit = "items" }
            : new ContentRangeHeaderValue(rangeStart, rangeEnd, totalLength.Value) { Unit = "items" };
        StatusCode = StatusCodes.Status206PartialContent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialContentResult"/> class with a pre-configured <see cref="ContentRangeHeaderValue"/>.
    /// </summary>
    /// <param name="contentRangeHeaderValue">The Content-Range header value to use in the response.</param>
    /// <param name="value">The partial data to include in the response body.</param>
    /// <remarks>
    /// <para>
    /// This constructor allows full control over the Content-Range header, including:
    /// <list type="bullet">
    /// <item>Custom unit values (e.g., "bytes" for file downloads, "items" for collections)</item>
    /// <item>Pre-computed range values from domain models</item>
    /// <item>Complex range scenarios not covered by the simpler constructor</item>
    /// </list>
    /// </para>
    /// <para>
    /// Use this constructor when you need to specify a unit other than "items",
    /// or when the Content-Range information is already available as a <see cref="ContentRangeHeaderValue"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// Using with byte ranges for file downloads:
    /// <code>
    /// var contentRange = new ContentRangeHeaderValue(0, 1023, 2048)
    /// {
    ///     Unit = "bytes"
    /// };
    /// return new PartialContentResult(contentRange, fileBytes);
    /// // Content-Range: bytes 0-1023/2048
    /// </code>
    /// </example>
    /// <example>
    /// Using with a domain model that includes range information:
    /// <code>
    /// var pagedResult = await _repository.GetPagedAsync(page, pageSize);
    /// var contentRange = new ContentRangeHeaderValue(
    ///     pagedResult.From,
    ///     pagedResult.To,
    ///     pagedResult.TotalCount)
    /// {
    ///     Unit = "items"
    /// };
    /// return new PartialContentResult(contentRange, pagedResult.Items);
    /// </code>
    /// </example>
    public PartialContentResult(ContentRangeHeaderValue contentRangeHeaderValue, object? value)
        : base(value)
    {
        _contentRangeHeaderValue = contentRangeHeaderValue;
        StatusCode = StatusCodes.Status206PartialContent;
    }

    /// <summary>
    /// Gets the Content-Range header value that will be included in the response.
    /// </summary>
    /// <value>
    /// The <see cref="ContentRangeHeaderValue"/> containing range and total length information.
    /// </value>
    /// <remarks>
    /// This property can be used to inspect the range information before the response is sent,
    /// for example in middleware or filters.
    /// </remarks>
    public ContentRangeHeaderValue ContentRangeHeaderValue => _contentRangeHeaderValue;

    /// <summary>
    /// Called during the formatting of the action result to add the Content-Range header to the HTTP response.
    /// </summary>
    /// <param name="context">The action context containing the HTTP context and response.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="context"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method is called by the ASP.NET Core framework as part of the response formatting pipeline.
    /// It adds the Content-Range header to the response headers before the response is sent to the client.
    /// </para>
    /// <para>
    /// The Content-Range header format follows RFC 7233:
    /// <c>Content-Range: {unit} {from}-{to}/{total}</c>
    /// </para>
    /// <para>
    /// You should not need to call this method directly; it's invoked automatically by the framework.
    /// </para>
    /// </remarks>
    public override void OnFormatting(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.OnFormatting(context);

        context.HttpContext.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.ContentRange] = _contentRangeHeaderValue.ToString();
    }
}