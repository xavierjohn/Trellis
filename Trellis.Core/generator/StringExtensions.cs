namespace SourceGenerator;

using System.Text.RegularExpressions;

/// <summary>
/// Provides string manipulation utilities for the source generator.
/// </summary>
/// <remarks>
/// These extension methods help format class names and property names for generated code
/// and error messages, ensuring consistent naming conventions and readability.
/// </remarks>
internal static class StringExtensions
{
    /// <summary>
    /// Splits PascalCase strings into space-separated words for human-readable error messages.
    /// </summary>
    /// <param name="input">The PascalCase string to split (e.g., "FirstName", "EmailAddress").</param>
    /// <returns>
    /// A space-separated version of the input string (e.g., "First Name", "Email Address").
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is used to generate friendly validation error messages by converting
    /// class names like "FirstName" into readable phrases like "First Name cannot be empty."
    /// </para>
    /// <para>
    /// Examples:
    /// <list type="bullet">
    /// <item>"FirstName" → "First Name"</item>
    /// <item>"EmailAddress" → "Email Address"</item>
    /// <item>"OrderId" → "Order Id"</item>
    /// <item>"SKU" → "SKU" (no change for all caps)</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static string SplitPascalCase(this string input)
        => Regex.Replace(input, @"(?<=[a-z])(?=[A-Z])", " ").Trim();

    /// <summary>
    /// Converts a PascalCase string to camelCase by lowercasing the first character.
    /// </summary>
    /// <param name="str">The PascalCase string to convert (e.g., "FirstName", "Email").</param>
    /// <returns>
    /// A camelCase version of the string (e.g., "firstName", "email").
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is used to generate field names for validation errors that match
    /// JSON property naming conventions (camelCase).
    /// </para>
    /// <para>
    /// Examples:
    /// <list type="bullet">
    /// <item>"FirstName" → "firstName"</item>
    /// <item>"Email" → "email"</item>
    /// <item>"ID" → "iD" (only first char lowercased)</item>
    /// <item>"" → ""</item>
    /// </list>
    /// </para>
    /// <para>
    /// For single-character strings, returns the lowercase version.
    /// For null or empty strings, returns the lowercase version.
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

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}