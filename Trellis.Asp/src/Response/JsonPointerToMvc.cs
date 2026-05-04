namespace Trellis.Asp;

using System;
using System.Text;

/// <summary>
/// Translates an RFC 6901 JSON Pointer (e.g. <c>/items/0/name</c>) into the
/// dot+bracket field-key convention used by ASP.NET Core's default
/// <c>ValidationProblemDetails</c> (e.g. <c>items[0].name</c>).
/// </summary>
/// <remarks>
/// <para>
/// The wire shape produced matches what <c>[ApiController]</c> emits in its
/// automatic 400 responses, so OpenAPI codegen consumers (axios + react-query,
/// NSwag, etc.) and React form libraries (react-hook-form, Formik) can lookup
/// errors by the same field name they use client-side.
/// </para>
/// <para>
/// JSON Pointer escape sequences are decoded per RFC 6901 §4: <c>~1</c> first
/// becomes <c>/</c>, then <c>~0</c> becomes <c>~</c> (order matters to avoid
/// turning <c>~01</c> into <c>/</c>). Numeric segments are rendered as
/// <c>[N]</c>; non-numeric segments are joined with a dot. A leading numeric
/// segment becomes <c>[N]</c> (root array convention). Per RFC 6901, a numeric
/// segment with leading zeros (e.g. <c>01</c>) is treated as a property name,
/// not an array index. Empty reference tokens (e.g. <c>/</c> = a single
/// empty-named member, <c>/foo/</c> = an empty-named member of <c>foo</c>) are
/// emitted as the MVC indexer-with-empty-string form <c>[""]</c>, matching
/// ASP.NET Core's own treatment of dictionary access with an empty key. This
/// keeps <c>""</c> (root) distinct from <c>/</c> on the wire.
/// </para>
/// <para>
/// <b>Inherent ambiguity of MVC convention.</b> The dot+bracket wire format
/// cannot losslessly encode every JSON Pointer:
/// </para>
/// <list type="bullet">
/// <item>
/// A numeric reference token is always emitted as <c>[N]</c> regardless of
/// whether the underlying container is an array or a numeric-keyed dictionary,
/// because the translator does not have access to the schema. This matches
/// what <c>[ApiController]</c> emits for both <c>List&lt;T&gt;</c> and
/// <c>Dictionary&lt;int,T&gt;</c> property access.
/// </item>
/// <item>
/// Pointer segments that contain MVC syntax characters (<c>.</c>, <c>[</c>,
/// <c>]</c>) collapse with structurally distinct pointers — for example,
/// <c>/customer.email</c> (one segment) and <c>/customer/email</c> (two
/// segments) both translate to <c>customer.email</c>. C# property names
/// reflected by FluentValidation cannot contain these characters, but
/// <see cref="InputPointer.ForProperty(string)"/> only escapes <c>~</c> and
/// <c>/</c> per RFC 6901, so any caller that passes a propertyName containing
/// <c>.</c>, <c>[</c>, or <c>]</c> (for example a custom adapter mapping a
/// dotted path to a flat field) will produce a single-segment pointer that
/// collides with structurally distinct multi-segment pointers under this
/// translation. Raw JSON Pointer values are preserved per-rule on
/// <c>extensions["rules"][n].fields[]</c> for any
/// <see cref="Error.UnprocessableContent"/> that includes
/// <see cref="RuleViolation"/> entries; <c>FieldViolation</c>-only payloads
/// only carry the translated MVC key on the wire and have no escape hatch.
/// Producers needing path fidelity for a flat field violation must therefore
/// also emit a corresponding <c>RuleViolation</c>.
/// </item>
/// </list>
/// </remarks>
internal static class JsonPointerToMvc
{
    /// <summary>
    /// Translates the given JSON Pointer to MVC dot+bracket convention.
    /// </summary>
    /// <param name="jsonPointer">A valid RFC 6901 JSON Pointer (empty or starting with <c>/</c>).</param>
    /// <returns>The MVC-convention field key, or the empty string for the root pointer.</returns>
    public static string Translate(string jsonPointer)
    {
        if (string.IsNullOrEmpty(jsonPointer))
            return string.Empty;

        // InputPointer guarantees a leading '/' for non-empty paths.
        var trimmed = jsonPointer[0] == '/' ? jsonPointer[1..] : jsonPointer;

        var segments = trimmed.Split('/');
        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            // RFC 6901 §4: decode '~1' before '~0' to avoid '~01' → '/' miscoding.
            var decoded = segments[i]
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (decoded.Length == 0)
            {
                // RFC 6901 allows empty reference tokens (members with empty names).
                // Emit MVC's indexer-with-empty-string form so "/" stays distinct from
                // root ("") and structurally-distinct pointers don't collapse silently.
                sb.Append("[\"\"]");
            }
            else if (IsArrayIndex(decoded))
            {
                sb.Append('[').Append(decoded).Append(']');
            }
            else
            {
                if (sb.Length > 0)
                    sb.Append('.');
                sb.Append(decoded);
            }
        }

        return sb.ToString();
    }

    private static bool IsArrayIndex(string segment)
    {
        if (segment.Length == 0)
            return false;
        // Per RFC 6901 §4, array indices are digit sequences without leading zeros
        // (except the single character "0" itself); anything else is a property name.
        if (segment.Length > 1 && segment[0] == '0')
            return false;
        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] is < '0' or > '9')
                return false;
        }

        return true;
    }
}
