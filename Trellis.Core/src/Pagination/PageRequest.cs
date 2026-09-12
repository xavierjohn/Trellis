namespace Trellis;

/// <summary>Validated transport-neutral pagination inputs, before decoding query-specific state.</summary>
public sealed record PageRequest
{
    private PageRequest(PageSize size, Cursor? cursor)
    {
        Size = size;
        Cursor = cursor;
    }

    /// <summary>The validated requested and applied limits.</summary>
    public PageSize Size { get; }

    /// <summary>The supplied opaque token, or null for the first page.</summary>
    public Cursor? Cursor { get; }

    /// <summary>Validates input without treating empty cursors or non-positive limits as absence.</summary>
    public static Result<PageRequest> TryCreate(
        string? cursor,
        int? limit,
        int max = PageSize.Max,
        int defaultSize = PageSize.Default,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp,
        string? cursorFieldName = null,
        string? limitFieldName = null)
    {
        var sizeResult = PageSize.TryCreate(limit, max, limitFieldName ?? "limit", policy, defaultSize);
        if (!sizeResult.TryGetValue(out var size, out var error))
            return Result.Fail<PageRequest>(error);
        if (cursor is null)
            return Result.Ok(new PageRequest(size, null));
        return Trellis.Cursor.TryCreate(cursor, cursorFieldName).Map(token => new PageRequest(size, token));
    }

    /// <summary>Decodes supplied continuation state; absence is success with no boundary.</summary>
    public Result<Maybe<TState>> Decode<TState>(ICursorCodec<TState> codec, string? fieldName = null)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(codec);
        return CursorCodec.TryDecodeOptional(Cursor, codec, fieldName);
    }
}
