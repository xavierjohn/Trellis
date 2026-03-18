namespace Trellis;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Provides internal string manipulation extension methods for Trellis primitives.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Normalizes an optional field name to camelCase, falling back to a default name.
    /// </summary>
    /// <param name="fieldName">The optional field name to normalize.</param>
    /// <param name="defaultName">The default field name if <paramref name="fieldName"/> is null or empty.</param>
    /// <returns>The camelCased field name, or <paramref name="defaultName"/> if not provided.</returns>
    public static string NormalizeFieldName(this string? fieldName, string defaultName) =>
        !string.IsNullOrEmpty(fieldName) ? fieldName.ToCamelCase() : defaultName;

    /// <summary>
    /// Parses a string value using the specified <see cref="IScalarValue{TSelf, TPrimitive}"/> factory.
    /// Throws <see cref="FormatException"/> if the value is invalid.
    /// </summary>
    public static T ParseScalarValue<T>(string? s) where T : class, IScalarValue<T, string>
    {
        var r = T.TryCreate(s!);
        if (r.IsFailure)
        {
            if (r.Error is ValidationError val && val.FieldErrors.Length > 0 && val.FieldErrors[0].Details.Length > 0)
                throw new FormatException(val.FieldErrors[0].Details[0]);

            throw new FormatException(r.Error.Detail);
        }

        return r.Value;
    }

    /// <summary>
    /// Tries to parse a string value using the specified <see cref="IScalarValue{TSelf, TPrimitive}"/> factory.
    /// </summary>
    public static bool TryParseScalarValue<T>([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out T result) where T : class, IScalarValue<T, string>
    {
        var r = T.TryCreate(s!);
        if (r.IsFailure)
        {
            result = default;
            return false;
        }

        result = r.Value;
        return true;
    }

    /// <summary>
    /// Converts the first character of a string to lowercase while preserving the rest of the string.
    /// Used primarily for converting C# PascalCase property names to camelCase for JSON serialization and validation error field names.
    /// </summary>
    /// <param name="str">The string to convert to camelCase.</param>
    /// <returns>
    /// A new string with the first character in lowercase and the remaining characters unchanged.
    /// If the string is null or empty, returns <see cref="string.Empty"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is used internally to ensure consistent field naming in validation errors and JSON responses.
    /// For example, converting "Email" to "email" or "FirstName" to "firstName".
    /// </para>
    /// <para>
    /// Examples:
    /// <list type="bullet">
    /// <item>"Email" → "email"</item>
    /// <item>"FirstName" → "firstName"</item>
    /// <item>"A" → "a"</item>
    /// <item>"" → ""</item>
    /// <item>&lt;c&gt;(string)null&lt;/c&gt; → ""</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static string ToCamelCase(this string? str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        var value = str!;

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}