namespace Trellis;

using System;

/// <summary>
/// Overrides the naive plural form used when the framework synthesises a canonical resource URI
/// for <c>ProblemDetails.Instance</c> from a <see cref="ResourceRef"/>. The default form is
/// <c>ResourceRef.Type.ToLowerInvariant() + "s"</c>; this attribute lets the consumer supply
/// an irregular plural (<c>Person</c> → <c>people</c>) or a domain-specific collection segment
/// (<c>UserAccount</c> → <c>accounts</c>).
/// </summary>
/// <remarks>
/// <para>
/// The attribute is read by <c>Trellis.Asp.AddResourceCollectionNames(Assembly)</c> at startup
/// and cached for the lifetime of the host. Apply it to the aggregate or resource type whose
/// CLR name is reported by <see cref="ResourceRef.For{TResource}(object?)"/>.
/// </para>
/// <para>
/// <see cref="Name"/> must be a single URL-safe path segment composed of RFC 3986
/// <c>unreserved</c> characters only: ASCII letters and digits plus <c>-</c>, <c>.</c>,
/// <c>_</c>, and <c>~</c>. Reserved characters (<c>/</c>, <c>?</c>, <c>#</c>), percent-encoded
/// triplets (<c>%2F</c>), <c>+</c>, and whitespace are rejected because the value is emitted
/// unencoded into the synthesised <c>Instance</c> URI and would otherwise change the URI's
/// shape or meaning. Validation happens in the attribute constructor so a misconfigured
/// override is surfaced at type-load (not at the first failing request).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ResourceCollectionName("people")]
/// public sealed class Person { /* ... */ }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class ResourceCollectionNameAttribute : Attribute
{
    /// <summary>The collection-name path segment to use in synthesised resource URIs.</summary>
    public string Name { get; }

    /// <summary>
    /// Initialises a new <see cref="ResourceCollectionNameAttribute"/>.
    /// </summary>
    /// <param name="name">The collection-name path segment.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty, whitespace, or contains any character
    /// outside the RFC 3986 <c>unreserved</c> set (ASCII letters and digits plus
    /// <c>-</c>, <c>.</c>, <c>_</c>, <c>~</c>).
    /// </exception>
    public ResourceCollectionNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsSafePathSegment(name))
        {
            throw new ArgumentException(
                $"Collection name '{name}' must be a single URL-safe path segment of RFC 3986 unreserved characters only (ASCII letters and digits, '-', '.', '_', '~').",
                nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="name"/> is a non-empty, non-whitespace string
    /// composed entirely of RFC 3986 <c>unreserved</c> characters (ASCII letters and digits
    /// plus <c>-</c>, <c>.</c>, <c>_</c>, <c>~</c>). Reserved characters (<c>/</c>, <c>?</c>,
    /// <c>#</c>), percent (<c>%</c>), <c>+</c>, and any whitespace return <c>false</c>.
    /// Exposed so that other framework components consuming a
    /// <see cref="ResourceCollectionNameAttribute"/>-equivalent override can apply the same
    /// validation rule.
    /// </summary>
    /// <param name="name">The candidate path segment. <c>null</c> returns <c>false</c>.</param>
    /// <returns><c>true</c> when the value is safe to use unencoded as a single path segment.</returns>
    public static bool IsSafePathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var c in name)
        {
            if (!IsUnreserved(c))
                return false;
        }

        return true;
    }

    private static bool IsUnreserved(char c)
        => c is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z')
            or (>= '0' and <= '9')
            or '-' or '.' or '_' or '~';
}