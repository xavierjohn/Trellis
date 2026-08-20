namespace Trellis.Asp.Validation;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Parses <see cref="System.Text.Json.JsonException.Path"/> (JSONPath syntax) once, so the MVC key
/// shape and the RFC 6901 pointer shape are two formattings of a single parse rather than two
/// parsers that merely look consistent.
/// </summary>
/// <remarks>
/// The parsing heuristics — and their known limitation for property names containing the literal
/// sequence <c>'][</c> — are documented on <see cref="ScalarValueValidationMiddleware.JsonPathToMvcKey"/>.
/// </remarks>
internal static class JsonPathSegments
{
    internal enum SegmentKind
    {
        /// <summary>A property name, already unquoted. May legitimately be empty.</summary>
        Property,

        /// <summary>An indexer, carrying both the literal text and its unbracketed content.</summary>
        Index,

        /// <summary>A character the grammar does not account for, preserved verbatim.</summary>
        Raw,
    }

    internal readonly record struct Segment(SegmentKind Kind, string Text, string Inner);

    /// <summary>
    /// Gets whether <paramref name="jsonExceptionPath"/> is a path this grammar can parse. A value
    /// that does not begin with <c>$</c> is passed through verbatim by the MVC formatter and has no
    /// meaningful pointer form.
    /// </summary>
    internal static bool IsParsable(string? jsonExceptionPath) =>
        !string.IsNullOrEmpty(jsonExceptionPath) && jsonExceptionPath[0] == '$';

    internal static List<Segment> Parse(string jsonExceptionPath)
    {
        var segments = new List<Segment>();
        var i = 1;
        while (i < jsonExceptionPath.Length)
        {
            var c = jsonExceptionPath[i];
            if (c == '.')
            {
                i++;
                var start = i;
                while (i < jsonExceptionPath.Length
                       && jsonExceptionPath[i] != '.'
                       && jsonExceptionPath[i] != '[')
                    i++;

                var name = i > start ? jsonExceptionPath[start..i] : string.Empty;
                segments.Add(new Segment(SegmentKind.Property, name, name));
            }
            else if (c == '[')
            {
                if (i + 1 < jsonExceptionPath.Length && jsonExceptionPath[i + 1] == '\'')
                {
                    var contentStart = i + 2;
                    var closeIdx = -1;
                    for (var j = contentStart; j + 1 < jsonExceptionPath.Length; j++)
                    {
                        if (jsonExceptionPath[j] != '\'') continue;
                        if (jsonExceptionPath[j + 1] != ']') continue;
                        var afterIdx = j + 2;
                        if (afterIdx == jsonExceptionPath.Length
                            || jsonExceptionPath[afterIdx] == '.'
                            || jsonExceptionPath[afterIdx] == '[')
                        {
                            closeIdx = j;
                            break;
                        }
                    }

                    string content;
                    if (closeIdx >= 0)
                    {
                        content = jsonExceptionPath[contentStart..closeIdx];
                        i = closeIdx + 2;
                    }
                    else
                    {
                        content = jsonExceptionPath[contentStart..];
                        i = jsonExceptionPath.Length;
                    }

                    segments.Add(new Segment(SegmentKind.Property, content, content));
                }
                else
                {
                    var start = i;
                    while (i < jsonExceptionPath.Length && jsonExceptionPath[i] != ']') i++;
                    if (i < jsonExceptionPath.Length) i++;

                    var text = jsonExceptionPath[start..i];
                    var inner = text.Trim('[', ']');
                    segments.Add(new Segment(SegmentKind.Index, text, inner));
                }
            }
            else
            {
                segments.Add(new Segment(SegmentKind.Raw, c.ToString(), string.Empty));
                i++;
            }
        }

        return segments;
    }

    /// <summary>
    /// Formats parsed segments as an MVC dot+bracket key.
    /// </summary>
    /// <remarks>
    /// An empty property name emits <c>[""]</c> to match <c>JsonPointerToMvc.Translate</c>'s output
    /// for the equivalent JSON Pointer (<c>/</c> → <c>[""]</c>).
    /// </remarks>
    internal static string ToMvcKey(List<Segment> segments)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Property when segment.Text.Length == 0:
                    sb.Append("[\"\"]");
                    break;
                case SegmentKind.Property:
                    if (sb.Length > 0) sb.Append('.');
                    sb.Append(segment.Text);
                    break;
                default:
                    sb.Append(segment.Text);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats parsed segments as an RFC 6901 pointer, for use as an ancestor prefix.
    /// </summary>
    /// <remarks>
    /// Escaping follows RFC 6901 §3: <c>~</c> first (to <c>~0</c>), then <c>/</c> (to <c>~1</c>).
    /// A <see cref="SegmentKind.Raw"/> segment means the grammar was not understood; it is dropped
    /// rather than guessed at, which under-prefixes instead of inventing a location.
    /// </remarks>
    internal static string ToPointer(List<Segment> segments)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.Kind == SegmentKind.Raw)
                continue;

            sb.Append('/').Append(Escape(segment.Inner));
        }

        return sb.ToString();
    }

    private static string Escape(string segment) =>
        segment.Replace("~", "~0", System.StringComparison.Ordinal)
               .Replace("/", "~1", System.StringComparison.Ordinal);
}
