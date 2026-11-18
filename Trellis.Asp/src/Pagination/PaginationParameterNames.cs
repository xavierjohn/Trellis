namespace Trellis.Asp;

internal static class PaginationParameterNames
{
    internal static void ThrowIfInvalid(string cursorParameter, string limitParameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursorParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(limitParameter);
        if (string.Equals(cursorParameter, limitParameter, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cursor and limit parameter names must be distinct.", nameof(limitParameter));
    }
}
