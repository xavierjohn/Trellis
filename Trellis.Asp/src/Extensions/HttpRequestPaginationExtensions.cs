namespace Trellis.Asp;

using System.Globalization;
using Microsoft.AspNetCore.Http;

/// <summary>Parses transport-neutral pagination values from ASP.NET Core requests.</summary>
public static class HttpRequestPaginationExtensions
{
    private const string CoreCursorPath = "/cursor";
    private const string CoreLimitName = "limit";
    private const string CoreLimitPath = "/limit";

    /// <summary>
    /// Parses a cursor and limit from raw query values while preserving the distinction between
    /// a missing cursor and a present empty cursor.
    /// </summary>
    /// <param name="request">The request whose query values are parsed.</param>
    /// <param name="cursorParameter">The query parameter containing the opaque cursor.</param>
    /// <param name="limitParameter">The query parameter containing the requested page size.</param>
    /// <param name="max">The maximum applied page size.</param>
    /// <param name="defaultSize">The page size used when <paramref name="limitParameter"/> is absent.</param>
    /// <param name="policy">How a requested page size above <paramref name="max"/> is handled.</param>
    /// <returns>
    /// A validated <see cref="PageRequest"/>, or an <see cref="Error.InvalidInput"/> whose field
    /// pointers identify the corresponding query parameters.
    /// </returns>
    /// <remarks>
    /// Each query parameter may occur at most once. The limit is parsed using invariant integer
    /// rules, then both values are validated by <see cref="PageRequest.TryCreate"/>.
    /// This extension is usable from Minimal API handlers and MVC actions, but does not add
    /// ApiExplorer or OpenAPI parameter metadata.
    /// Downstream cursor decoding remains transport-neutral; query-only endpoints should declare
    /// <see cref="InputLocation.Query"/> through endpoint input-origin metadata.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A parameter name is null, empty, or whitespace, or the two names differ only by case.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="max"/>, <paramref name="defaultSize"/>, or <paramref name="policy"/> is invalid.
    /// </exception>
    public static Result<PageRequest> TryCreatePageRequest(
        this HttpRequest request,
        string cursorParameter = "cursor",
        string limitParameter = "limit",
        int max = PageSize.Max,
        int defaultSize = PageSize.Default,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(cursorParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(limitParameter);
        if (string.Equals(cursorParameter, limitParameter, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cursor and limit parameter names must be distinct.", nameof(limitParameter));

        // Validate server-owned settings before malformed query text can return a client-shaped failure.
        var defaultRequest = PageRequest.TryCreate(
            cursor: null,
            limit: null,
            max,
            defaultSize,
            policy);

        var query = request.Query;
        var hasCursor = query.TryGetValue(cursorParameter, out var cursorValues);
        var hasLimit = query.TryGetValue(limitParameter, out var limitValues);
        if (!hasCursor && !hasLimit)
            return defaultRequest;

        int? limit = null;
        if (hasLimit)
        {
            if (limitValues.Count != 1)
                return CardinalityFailure(limitParameter, ValidationCodes.FormatInteger);

            var rawLimit = limitValues[0] ?? string.Empty;
            if (!int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
            {
                return Result.Fail<PageRequest>(Error.InvalidInput.ForField(
                    ValidationCodes.FormatInteger,
                    InputPointer.ForQuery(limitParameter),
                    detail: "The query parameter is not a valid integer."));
            }

            limit = parsedLimit;
        }

        if (!hasCursor)
        {
            return RelocateFailure(
                PageRequest.TryCreate(
                    cursor: null,
                    limit,
                    max,
                    defaultSize,
                    policy),
                cursorParameter,
                limitParameter);
        }

        if (cursorValues.Count != 1)
        {
            var sizeResult = PageRequest.TryCreate(
                cursor: null,
                limit,
                max,
                defaultSize,
                policy);
            if (sizeResult.IsFailure)
                return RelocateFailure(sizeResult, cursorParameter, limitParameter);

            return CardinalityFailure(cursorParameter, ValidationCodes.CursorMalformed);
        }

        var cursor = cursorValues[0] ?? string.Empty;
        var result = PageRequest.TryCreate(
            cursor,
            limit,
            max,
            defaultSize,
            policy);
        return RelocateFailure(result, cursorParameter, limitParameter);
    }

    private static Result<PageRequest> CardinalityFailure(string parameter, string code) =>
        Result.Fail<PageRequest>(Error.InvalidInput.ForField(
            code,
            InputPointer.ForQuery(parameter),
            detail: $"The '{parameter}' query parameter must be specified at most once."));

    private static Result<PageRequest> RelocateFailure(
        Result<PageRequest> result,
        string cursorParameter,
        string limitParameter)
    {
        if (result.TryGetValue(out _, out var error))
            return result;

        return Result.Fail<PageRequest>(RelocateError(error, cursorParameter, limitParameter));
    }

    private static Error RelocateError(
        Error error,
        string cursorParameter,
        string limitParameter)
    {
        if (error is not Error.InvalidInput invalid)
            return error;

        return invalid with
        {
            Fields = EquatableArray.From(invalid.Fields.Items.Select(violation =>
                RelocateFieldViolation(violation, cursorParameter, limitParameter))),
            Rules = EquatableArray.From(invalid.Rules.Items.Select(violation =>
                violation with
                {
                    Fields = EquatableArray.From(violation.Fields.Items.Select(pointer =>
                        RelocatePointer(pointer, cursorParameter, limitParameter)))
                }))
        };
    }

    private static FieldViolation RelocateFieldViolation(
        FieldViolation violation,
        string cursorParameter,
        string limitParameter)
    {
        var detail = violation.Detail;
        if (violation.Field.In == InputLocation.Unspecified
            && violation.Field.Path == CoreLimitPath
            && !string.Equals(limitParameter, CoreLimitName, StringComparison.Ordinal)
            && detail?.StartsWith(CoreLimitName + " ", StringComparison.Ordinal) == true)
        {
            detail = limitParameter + detail[CoreLimitName.Length..];
        }

        return violation with
        {
            Field = RelocatePointer(violation.Field, cursorParameter, limitParameter),
            Detail = detail
        };
    }

    private static InputPointer RelocatePointer(
        InputPointer pointer,
        string cursorParameter,
        string limitParameter)
    {
        if (pointer.In != InputLocation.Unspecified)
            return pointer;

        // Core uses canonical property paths; this HTTP boundary assigns the configured query names.
        return pointer.Path switch
        {
            CoreCursorPath => InputPointer.ForQuery(cursorParameter),
            CoreLimitPath => InputPointer.ForQuery(limitParameter),
            _ => pointer
        };
    }
}
