namespace Trellis.FluentValidation;

using System.Text;

/// <summary>
/// Converts FluentValidation member-chain property names (e.g., <c>Address.PostCode</c>,
/// <c>Items[0].Sku</c>) into RFC 6901 JSON Pointers (e.g., <c>/Address/PostCode</c>,
/// <c>/Items/0/Sku</c>) so they can be carried through Trellis <see cref="InputPointer"/>
/// values without losing structure.
/// </summary>
internal static class JsonPointerNormalizer
{
    /// <summary>
    /// Converts a FluentValidation <c>PropertyName</c> to an RFC 6901 JSON Pointer string.
    /// </summary>
    /// <param name="propertyName">
    /// The FluentValidation property name. May contain dotted member chains
    /// (<c>Address.PostCode</c>) and indexer expressions (<c>Items[0].Sku</c>).
    /// </param>
    /// <returns>
    /// An RFC 6901 JSON Pointer string. Returns <c>""</c> for null/empty input. Inputs that
    /// already start with <c>"/"</c> are assumed to already be pointers and are returned
    /// unchanged.
    /// </returns>
    public static string ToJsonPointer(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return string.Empty;

        if (propertyName[0] == '/')
            return propertyName;

        var sb = new StringBuilder(propertyName.Length + 1);
        var i = 0;
        while (i < propertyName.Length)
        {
            sb.Append('/');

            i = propertyName[i] == '['
                ? AppendIndexer(propertyName, i, sb)
                : AppendSegment(propertyName, i, sb);

            if (i < propertyName.Length && propertyName[i] == '.')
                i++;
        }

        return sb.ToString();
    }

    private static int AppendSegment(string source, int i, StringBuilder sb)
    {
        while (i < source.Length && source[i] != '.' && source[i] != '[')
        {
            var c = source[i];
            if (c == '~')
                sb.Append("~0");
            else if (c == '/')
                sb.Append("~1");
            else
                sb.Append(c);
            i++;
        }

        return i;
    }

    private static int AppendIndexer(string source, int i, StringBuilder sb)
    {
        i++;
        var start = i;
        while (i < source.Length && source[i] != ']')
            i++;

        var indexContent = source.AsSpan(start, i - start);
        foreach (var c in indexContent)
        {
            if (c == '~')
                sb.Append("~0");
            else if (c == '/')
                sb.Append("~1");
            else
                sb.Append(c);
        }

        if (i < source.Length)
            i++;

        return i;
    }
}
