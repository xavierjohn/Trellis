namespace Trellis.Asp;

/// <summary>
/// Describes cursor pagination query parameters without binding or parsing request values.
/// </summary>
/// <remarks>
/// Apply to an MVC action or controller, or use
/// <see cref="PaginationEndpointConventionBuilderExtensions"/> for Minimal APIs.
/// Keep these settings aligned with the explicit
/// <see cref="HttpRequestPaginationExtensions.TryCreatePageRequest"/> call.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PaginationAttribute : Attribute
{
    /// <summary>Initializes a pagination declaration for native ASP.NET Core OpenAPI.</summary>
    /// <param name="cursorParameter">The query parameter containing the opaque cursor.</param>
    /// <param name="limitParameter">The query parameter containing the requested page size.</param>
    /// <param name="max">The maximum applied page size.</param>
    /// <param name="defaultSize">The requested page size when the limit is absent.</param>
    /// <param name="policy">How explicitly requested page sizes above the maximum are handled.</param>
    /// <exception cref="ArgumentException">
    /// A parameter name is null, empty, or whitespace, or the names differ only by case.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The maximum, default size, or policy is invalid.
    /// </exception>
    public PaginationAttribute(
        string cursorParameter = "cursor",
        string limitParameter = "limit",
        int max = PageSize.Max,
        int defaultSize = PageSize.Default,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursorParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(limitParameter);
        if (string.Equals(cursorParameter, limitParameter, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cursor and limit parameter names must be distinct.", nameof(limitParameter));
        if (max is <= 0 or > PageSize.MaxApplied)
            throw new ArgumentOutOfRangeException(nameof(max), "Max must allow over-fetching one additional row.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultSize);
        if (policy is not PageSizeLimitPolicy.Clamp and not PageSizeLimitPolicy.Reject)
            throw new ArgumentOutOfRangeException(nameof(policy));

        CursorParameter = cursorParameter;
        LimitParameter = limitParameter;
        Max = max;
        DefaultSize = defaultSize;
        Policy = policy;
    }

    /// <summary>Gets the name of the cursor query parameter.</summary>
    public string CursorParameter { get; }

    /// <summary>Gets the name of the page-size query parameter.</summary>
    public string LimitParameter { get; }

    /// <summary>Gets the maximum applied page size.</summary>
    public int Max { get; }

    /// <summary>Gets the requested page size when no limit is supplied.</summary>
    public int DefaultSize { get; }

    /// <summary>Gets the policy for an explicitly requested size above the maximum.</summary>
    public PageSizeLimitPolicy Policy { get; }
}
