---
name: recipe-39-localized-validation
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows rendering a validation failure in the caller's language from its code and args. Use when localizing validation messages instead of parsing English detail."
---
# Recipe 39 — Rendering a validation failure in the caller's language (`code` + `args`)

**Requires:** `Trellis.Core`, plus `Trellis.Asp`.

## Contents

- Problem
- What it shows
- Anti-pattern → fix

**Problem.** A 422 arrives carrying `detail` in English, and the UI must show the message in the user's language. The tempting shortcut is to scrape the sentence — read the numbers out of `"must be between 0 and 150"`, or split `"Valid values: Checking, MoneyMarket, Savings"` on `", "` — and that shortcut is what `code` and `args` exist to retire. Prose belongs to whichever producer noticed the failure, and two producers may notice the same failure: an unknown enum name is worded `'Platinum' is not a valid AccountType. Valid values: …` by `RequiredEnum.TryCreate` and `Invalid AccountType value: 'Platinum'. Valid values are: …` by `RequiredEnumJsonConverter`, depending on how the value reached the model. A scraper built against one wording silently mis-renders the other. `Code` is stable, and `Args` carries the operands as typed values — so render from those and treat `Detail` only as a fallback.

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Trellis;
using Trellis.Asp;

// Resource keys are reason codes, so the .resx *is* the vocabulary:
//   value.between-inclusive   = "Must be between {from} and {to}."
//   string.max-length         = "Use at most {maxLength} characters."
//   enum.name-undefined       = "Choose one of: {allowed}."
//   enum.name-undefined.count = "That is not one of the {allowedCount} permitted values."
//   enum.name-undefined.bare  = "That is not a permitted value."
//   enum.undefined            = "Choose one of: {allowed}."
//   enum.undefined.count      = "That is not one of the {allowedCount} permitted values."
//   enum.undefined.bare       = "That is not a permitted value."
//   value.not-empty           = "This field cannot be blank."
//   error.unspecified         = "That value is not valid."
//
// Two further keys are *not* reason codes: bool.true and bool.false render boolean args,
// because a raw "True" is not a sentence in any language.
//
// The rows above are illustrative: a real table carries one row per code the API can emit,
// and a row per .count/.bare variant of the two enum codes.
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

        // Unreachable while the union stays closed. Throwing rather than returning "" is what
        // makes the exhaustiveness real: a case added by a future upgrade surfaces here instead
        // of silently deleting an operand from a translated sentence.
        _ => throw new NotSupportedException($"Unhandled arg value: {value.GetType().Name}."),
    };

    // Projected with a loop rather than LINQ: `Items` is an EquatableArray, which deliberately
    // does not implement IEnumerable<T>, so Enumerable.Select never applies. The only `Select`
    // in scope is then MaybeLinqExtensions', and inference fails with CS0411.
    private string FormatList(ValidationArgValue.List list, CultureInfo culture)
    {
        var parts = new string[list.Items.Length];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Format(list.Items[i], culture);

        return string.Join(culture.TextInfo.ListSeparator + " ", parts);
    }
}
```

Read the violations off the response, not the root, and do not assume they are there:

```csharp
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
```

`FieldViolationProblemDetail` and `ValidationArgValue` are declared in `Trellis.Asp` and `Trellis.Core`, which a Trellis-based caller already references — but nothing here depends on that. The contract is the JSON, so a client on another stack declares its own equivalent record and the recipe is unchanged; these are response-shape metadata, not a domain model to share.

**What it shows.** A localized message is a function of `Code`, `Args`, and a resource table — never of `Detail`. Keying the `.resx` on the reason code makes the [`ValidationCodes`](../trellis-api-core.md#validationcodes--the-reason-code-vocabulary) vocabulary the translation vocabulary, so a missing translation is a missing row rather than a parsing bug, and a translator sees the whole surface in one file. Because `ValidationArgValue` is a closed union of JSON's self-describing values, formatting is total and exhaustive: `Number` is a `decimal` written invariantly on the wire and formatted *culturally* for display, which is the one conversion a scraper cannot do correctly because it never recovers the type. `ViolationLocation.Pointer` (RFC 6901) is what binds the message back to a form control.

The same renderer works server-side when an API must localize on behalf of thin clients — resolve the culture from `Accept-Language` and render before writing the response. Prefer the client doing it: the server cannot know the user's locale better than the client, and a localized `detail` is not cacheable across languages.

**Anti-pattern → fix.**

| Anti-pattern | Why it breaks | Fix |
|---|---|---|
| Parsing operands out of `Detail` (`"Valid values: "`, `"between 0 and 150"`) | Prose belongs to the producer, and one failure has several producers with different wording. It is also English-only, which is the problem being solved. | Read `Args["from"]`, `Args["allowed"]`. |
| Treating a missing `allowed` as "nothing is permitted" | Past `ValidationArgs.MaxAllowedMembers` the list is omitted deliberately and `allowedCount` sent instead. Rendering "choose one of: (none)" tells the user the field is unusable. | Branch on `allowed` / `allowedCount` / neither. |
| Switching on the root `code` | `Error.InvalidInput` leaves the root at `error.unspecified`; the actionable codes are per violation. | Descend into `fieldViolations[n].code`. |
| Assuming `fieldViolations` exists | A body that never parsed reports 400 with no such member. | Treat absence as "no per-field reason available". |
| Formatting a `Number` with `InvariantCulture` for display, or echoing the raw JSON text | Shows `1.5` to a user whose locale writes `1,5`. | Format with the UI culture; the wire stays invariant. |
| Falling back to a generic string for an unrecognized code | Discards the server's own explanation, which is usually better than "Invalid input". | Fall back to `Detail` first, generic prose last. |
