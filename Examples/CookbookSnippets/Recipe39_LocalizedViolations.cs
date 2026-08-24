// Cookbook Recipe 39 — Rendering a validation failure in the caller's language (code + args).
namespace CookbookSnippets.Recipe39;

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Resource marker for the violation message table. Resource keys are reason codes, so the
/// .resx *is* the vocabulary:
/// <code>
///   value.between-inclusive   = "Must be between {from} and {to}."
///   string.max-length         = "Use at most {maxLength} characters."
///   enum.name-undefined       = "Choose one of: {allowed}."
///   enum.name-undefined.count = "That is not one of the {allowedCount} permitted values."
///   enum.name-undefined.bare  = "That is not a permitted value."
///   value.not-empty           = "This field cannot be blank."
///   error.unspecified         = "That value is not valid."
/// </code>
/// </summary>
public sealed class ViolationMessages;

public sealed class ViolationMessageRenderer(IStringLocalizer<ViolationMessages> localizer)
{
    public string Render(FieldViolationProblemDetail violation, CultureInfo culture)
    {
        var template = localizer[TemplateKey(violation)];

        // An unknown code is not a bug to hide: a server may add a code before this client
        // learns it. The server's own sentence is the best available answer, and only when
        // it is absent too does the caller get a generic one -- which is why
        // error.unspecified is the one row the table must carry.
        return template.ResourceNotFound
            ? violation.Detail ?? localizer[ValidationCodes.Unspecified].Value
            : Expand(template.Value, violation.Args, culture);
    }

    // `allowed` is dropped whole past ValidationArgs.MaxAllowedMembers and replaced by
    // `allowedCount`, so an enum rejection has three renderings, not one. A missing `allowed`
    // means "not supplied" — never "nothing is permitted".
    private static string TemplateKey(FieldViolationProblemDetail violation)
    {
        if (violation.Code is not (ValidationCodes.EnumNameUndefined or ValidationCodes.EnumUndefined))
            return violation.Code;
        if (violation.Args?.ContainsKey("allowed") == true) return violation.Code;

        return violation.Args?.ContainsKey("allowedCount") == true
            ? violation.Code + ".count"
            : violation.Code + ".bare";
    }

    private string Expand(
        string template,
        IReadOnlyDictionary<string, ValidationArgValue>? args,
        CultureInfo culture)
    {
        if (args is null || !template.Contains('{')) return template;

        var rendered = new StringBuilder(template.Length);
        var rest = template.AsSpan();
        while (true)
        {
            var open = rest.IndexOf('{');
            var close = open < 0 ? -1 : rest[open..].IndexOf('}');
            if (open < 0 || close < 0)
            {
                rendered.Append(rest);
                return rendered.ToString();
            }

            rendered.Append(rest[..open]);
            var name = rest.Slice(open + 1, close - 1).ToString();
            rendered.Append(args.TryGetValue(name, out var value)
                ? Format(value, culture)
                : rest.Slice(open, close + 1));   // leave an unmatched placeholder visible
            rest = rest[(open + close + 1)..];
        }
    }

    // The union is closed, so this switch covers every shape an arg can take: a new case
    // cannot appear without this method needing an arm for it.
    private string Format(ValidationArgValue value, CultureInfo culture) => value switch
    {
        ValidationArgValue.Text text => text.Value,

        // Invariant on the wire, cultural on screen — a German user reads "1,5", not "1.5".
        ValidationArgValue.Number number => number.Value.ToString("G29", culture),

        // A raw "True" is not a sentence in any language, so booleans go through the table too.
        ValidationArgValue.Bool flag => localizer[flag.Value ? "bool.true" : "bool.false"].Value,

        ValidationArgValue.List list => FormatList(list, culture),
        _ => string.Empty,
    };

    // Projected with a loop rather than LINQ: `Items` is an ImmutableArray, and with `using
    // Trellis;` in scope a `.Select(...)` on it binds ambiguously against MaybeLinqExtensions.
    private string FormatList(ValidationArgValue.List list, CultureInfo culture)
    {
        var parts = new string[list.Items.Length];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Format(list.Items[i], culture);

        return string.Join(culture.TextInfo.ListSeparator + " ", parts);
    }
}

public static class ViolationReader
{
    /// <summary>
    /// The root <c>code</c> on a validation failure is the <c>error.unspecified</c> sentinel by
    /// design — the reasons are per-violation. And a body that never parsed reports 400 with no
    /// <c>fieldViolations</c> member at all, which is "no per-field reason available", not a
    /// malformed response.
    /// </summary>
    public static FieldViolationProblemDetail[] ReadFieldViolations(
        ProblemDetails problem,
        JsonSerializerOptions options) =>
        problem.Extensions.TryGetValue("fieldViolations", out var raw) && raw is JsonElement element
            ? element.Deserialize<FieldViolationProblemDetail[]>(options) ?? []
            : [];

    public static IEnumerable<(string? Pointer, string Message)> Localize(
        ProblemDetails problem,
        ViolationMessageRenderer renderer,
        JsonSerializerOptions options,
        CultureInfo culture) =>
        ReadFieldViolations(problem, options)
            .Select(violation => (violation.Location.Pointer, renderer.Render(violation, culture)));
}
