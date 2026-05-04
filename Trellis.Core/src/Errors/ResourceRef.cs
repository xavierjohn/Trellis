namespace Trellis;

using System.Globalization;

/// <summary>
/// Identifies a resource by its type and optional identifier. Used as the typed payload
/// for resource-oriented errors such as <see cref="Error.NotFound"/>, <see cref="Error.Conflict"/>,
/// <see cref="Error.Gone"/>, and <see cref="Error.PreconditionFailed"/>.
/// </summary>
/// <param name="Type">
/// The resource type name (e.g. <c>"User"</c>, <c>"Order"</c>). Use
/// <see cref="For{TResource}(object?)"/> when the CLR type name is the desired resource name,
/// or <see cref="For(string, object?)"/> when a custom domain name is needed. Required.
/// </param>
/// <param name="Id">
/// Optional identifier of the specific resource instance. May be null when the error
/// applies to the resource collection rather than a specific instance.
/// </param>
public readonly record struct ResourceRef(string Type, string? Id = null)
{
    /// <summary>
    /// Creates a resource reference from an explicit resource type name and optional identifier.
    /// </summary>
    /// <param name="type">The resource type name.</param>
    /// <param name="id">Optional resource identifier.</param>
    /// <returns>A resource reference.</returns>
    public static ResourceRef For(string type, object? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new(type, FormatId(id));
    }

    /// <summary>
    /// Creates a resource reference whose resource type name is derived from
    /// <typeparamref name="TResource"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource type.</typeparam>
    /// <param name="id">Optional resource identifier.</param>
    /// <returns>A resource reference.</returns>
    /// <remarks>
    /// <para>
    /// The resource type name is the simple CLR name with two normalisations applied:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b>Backtick mangling stripped.</b> A closed generic such as <c>List&lt;User&gt;</c>
    /// reports a CLR <see cref="System.Reflection.MemberInfo.Name">Name</see> of
    /// <c>"List`1"</c>; the backtick suffix is removed so the resource type is <c>"List"</c>.
    /// Closed generics with multiple type arguments collapse to the outer simple name
    /// (e.g. <c>Dictionary&lt;string,int&gt;</c> → <c>"Dictionary"</c>). When the inner type
    /// argument is the meaningful resource identifier, prefer <see cref="For(string, object?)"/>
    /// with an explicit name.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b><see cref="Maybe{T}"/> wrappers are peeled.</b> <c>Maybe&lt;Order&gt;</c> reports
    /// <c>"Order"</c>, and the peeling is recursive (<c>Maybe&lt;Maybe&lt;Order&gt;&gt;</c>
    /// also reports <c>"Order"</c>). This avoids the CLR-mangled <c>"Maybe`1"</c> from
    /// leaking onto the wire when a result type happens to wrap its domain in
    /// <see cref="Maybe{T}"/> (e.g. <c>Result&lt;Maybe&lt;Order&gt;&gt;.ToHttpResponse(...)</c>).
    /// Other generic wrappers are not peeled — see <see cref="FormatTypeName"/> if you need
    /// the formatting without the Maybe-peeling step.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public static ResourceRef For<TResource>(object? id = null) =>
        new(FormatTypeName(PeelMaybe(typeof(TResource))), FormatId(id));

    /// <summary>
    /// Returns the simple CLR name for <paramref name="type"/> with backtick-mangling
    /// stripped. Used by Trellis components that surface a type-derived identifier on the
    /// wire (e.g. AOT-generated JSON converter fallback messages).
    /// </summary>
    /// <param name="type">The type whose name should be formatted.</param>
    /// <returns>
    /// The simple name with any <c>`N</c> arity suffix removed. For example,
    /// <c>typeof(List&lt;int&gt;)</c> returns <c>"List"</c>; <c>typeof(Order)</c> returns
    /// <c>"Order"</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
    /// <remarks>
    /// This helper does <b>not</b> peel <see cref="Maybe{T}"/> wrappers — that is intentionally
    /// scoped to <see cref="For{TResource}(object?)"/>, which owns the resource-naming contract.
    /// </remarks>
    public static string FormatTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var name = type.Name;
        var tickIndex = name.IndexOf('`');
        return tickIndex < 0 ? name : name.Substring(0, tickIndex);
    }

    private static Type PeelMaybe(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Maybe<>)
            ? PeelMaybe(type.GetGenericArguments()[0])
            : type;

    private static string? FormatId(object? id) =>
        id switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => id.ToString(),
        };
}