namespace Trellis.Asp.Validation;

using System;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Re-roots the composite-relative pointers carried by a <see cref="TrellisJsonValidationException"/>
/// onto the live ancestor path, exactly once.
/// </summary>
/// <remarks>
/// <para>
/// <c>CompositeValueObjectJsonConverter</c> lives in <c>Trellis.Primitives</c>, which references only
/// <c>Trellis.Core</c>. It cannot see <see cref="ValidationErrorsContext"/>, so it emits pointers
/// relative to the composite value object and knows nothing about where that value object sits in the
/// document. Re-rooting is the ASP layer's job, and the path-tracking wrappers are the only place
/// where both the exception and the live ancestor stack are in scope at once.
/// </para>
/// <para>
/// <c>AncestorPointer()</c> returns the <em>entire</em> live stack rather than the single segment
/// owned by the current wrapper, and wrappers nest. Prefixing at every level would therefore yield
/// <c>/members/0/members/0/address/street</c> for a three-deep graph. So the innermost wrapper to see
/// an unmarked exception re-roots it and marks it; every wrapper outside that one rethrows untouched.
/// </para>
/// <para>
/// The discriminator is an explicit marker, never a string-prefix heuristic. "Skip if the pointer
/// already starts with the ancestor" looks equivalent and is not: a composite value object with a
/// property named <c>members</c>, nested under a collection named <c>members</c>, produces a
/// legitimate relative pointer that coincides with its own ancestor, and the heuristic would silently
/// refuse to prefix it.
/// </para>
/// </remarks>
internal static class JsonValidationPathRebase
{
    /// <summary>
    /// The <see cref="Exception.Data"/> key marking an exception whose pointers are already absolute.
    /// Owned by <c>Trellis.Asp</c> and never part of the wire contract.
    /// </summary>
    internal const string AbsolutePointersMarker = "Trellis.Asp.PointersAreAbsolute";

    /// <summary>
    /// The <see cref="Exception.Data"/> key carrying the RFC 6901 ancestor pointer that was live when
    /// the exception was re-rooted. Owned by <c>Trellis.Asp</c> and never part of the wire contract.
    /// </summary>
    /// <remarks>
    /// This is what the unstructured arm consumes instead of
    /// <see cref="System.Text.Json.JsonException.Path"/>. Once a path-tracking wrapper is installed,
    /// that property is no longer trustworthy: the wrapper deserializes the inner object through a
    /// nested <c>JsonSerializer</c> call, so System.Text.Json stamps the path relative to that nested
    /// root and the outer frames leave the now-non-null value alone. The ancestor stack is both
    /// correct and lossless, because it never round-trips through a parseable path string.
    /// </remarks>
    internal const string AbsolutePathKey = "Trellis.Asp.AbsolutePath";

    /// <summary>
    /// Gets whether the pointers carried by <paramref name="exception"/> have already been re-rooted.
    /// </summary>
    internal static bool IsMarked(Exception exception) =>
        exception.Data.Contains(AbsolutePointersMarker);

    /// <summary>
    /// Gets the ancestor pointer recorded when <paramref name="exception"/> was re-rooted, or
    /// <see langword="null"/> when it never passed a path-tracking wrapper.
    /// </summary>
    internal static string? RecordedAbsolutePath(Exception exception) =>
        exception.Data[AbsolutePathKey] as string;

    /// <summary>
    /// Re-roots <paramref name="exception"/> unless it is already marked, in which case the original
    /// instance is returned unchanged.
    /// </summary>
    internal static TrellisJsonValidationException RebaseIfUnmarked(TrellisJsonValidationException exception) =>
        IsMarked(exception) ? exception : Rebase(exception);

    /// <summary>
    /// Re-roots <paramref name="exception"/> onto the live ancestor path and marks the result.
    /// </summary>
    /// <remarks>
    /// An exception carrying no <see cref="TrellisJsonValidationException.InvalidInput"/> has no
    /// pointers to rebase, but is still rewrapped so the live ancestor is recorded: it is the only
    /// remaining source of position once a wrapper has displaced
    /// <see cref="System.Text.Json.JsonException.Path"/>.
    /// </remarks>
    internal static TrellisJsonValidationException Rebase(TrellisJsonValidationException exception)
    {
        var ancestor = ValidationErrorsContext.CurrentAncestorPointer();

        // InvalidInput is { get; init; }, so the payload cannot be replaced on the original
        // instance — a new exception is constructed with the original chained as inner.
        var rebased = new TrellisJsonValidationException(exception.Message, exception)
        {
            InvalidInput = exception.InvalidInput is { } invalidInput
                ? RebaseInvalidInput(invalidInput, ancestor)
                : null,
        };

        rebased.Data[AbsolutePointersMarker] = true;
        rebased.Data[AbsolutePathKey] = ancestor;
        return rebased;
    }

    private static Error.InvalidInput RebaseInvalidInput(Error.InvalidInput invalidInput, string ancestor) =>
        RebaseTo(invalidInput, ancestor);

    /// <summary>
    /// Applies the body-context rebase rule to every pointer in <paramref name="invalidInput"/>
    /// against an explicitly supplied <paramref name="ancestor"/>.
    /// </summary>
    /// <remarks>
    /// Used directly by arm 2 of the path-resolution rule, where the ancestor comes from
    /// <see cref="System.Text.Json.JsonException.Path"/> rather than from the live stack — the
    /// failure never passed a wrapper, so there is no live stack by the time it is caught.
    /// </remarks>
    internal static Error.InvalidInput RebaseTo(Error.InvalidInput invalidInput, string ancestor)
    {
        var fields = invalidInput.Fields.Items
            .Select(violation =>
            {
                var rebased = ValidationErrorsContext.RebaseToBody(violation.Field, ancestor);
                return rebased == violation.Field ? violation : violation with { Field = rebased };
            })
            .ToImmutableArray();

        var rules = invalidInput.Rules.Items
            .Select(rule => rule.Fields.IsEmpty
                ? rule
                : rule with
                {
                    Fields = rule.Fields.Items
                        .Select(pointer => ValidationErrorsContext.RebaseToBody(pointer, ancestor))
                        .ToImmutableArray(),
                })
            .ToImmutableArray();

        return new Error.InvalidInput(fields, rules);
    }
}
