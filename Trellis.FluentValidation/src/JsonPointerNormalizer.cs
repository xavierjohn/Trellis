namespace Trellis.FluentValidation;

using System.Text;
using Trellis;

/// <summary>
/// Converts FluentValidation member-chain property names (e.g., <c>Address.PostCode</c>,
/// <c>Items[0].Sku</c>) into camelCase RFC 6901 JSON Pointers (e.g., <c>/address/postCode</c>,
/// <c>/items/0/sku</c>) so they can be carried through Trellis <see cref="InputPointer"/> values.
/// Each name segment's first character is lower-cased (via <see cref="StringExtensions.ToCamelCase"/>)
/// so FluentValidation error keys match the camelCase JSON wire and the rest of Trellis's validation
/// field names; indexer segments are left unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public surface.</b> Promoted from internal in the v3 package split so the
/// <c>Trellis.Mediator.FluentValidation</c> adapter (which lives in a separate assembly
/// after the split) can call <see cref="ToJsonPointer(string?)"/> across the package boundary
/// without forcing every consumer of <c>Trellis.FluentValidation</c> to take the Mediator
/// dependency. Third-party FluentValidation adapters that need to project FluentValidation
/// property names into <see cref="InputPointer"/> values can also use this helper directly.
/// </para>
/// </remarks>
public static class JsonPointerNormalizer
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
                : AppendNameSegment(propertyName, i, sb);

            if (i < propertyName.Length && propertyName[i] == '.')
                i++;
        }

        return sb.ToString();
    }

    private static int AppendNameSegment(string source, int i, StringBuilder sb)
    {
        var start = i;
        while (i < source.Length && source[i] != '.' && source[i] != '[')
            i++;

        AppendEscaped(source.Substring(start, i - start).ToCamelCase(), sb);
        return i;
    }

    private static int AppendIndexer(string source, int i, StringBuilder sb)
    {
        i++;
        var start = i;
        while (i < source.Length && source[i] != ']')
            i++;

        AppendEscaped(source.AsSpan(start, i - start), sb);

        if (i < source.Length)
            i++;

        return i;
    }

    private static void AppendEscaped(ReadOnlySpan<char> segment, StringBuilder sb)
    {
        foreach (var c in segment)
        {
            if (c == '~')
                sb.Append("~0");
            else if (c == '/')
                sb.Append("~1");
            else
                sb.Append(c);
        }
    }
}