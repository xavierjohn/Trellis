namespace Trellis;

using System.Collections.Immutable;
using System.Diagnostics;

/// <summary>
/// Combines two <see cref="Error"/> values into one.
/// Two <see cref="Error.InvalidInput"/> values merge their field/rule violations.
/// Otherwise the two errors are flattened into an <see cref="Error.Aggregate"/>.
/// </summary>
[DebuggerStepThrough]
public static class CombineErrorExtensions
{
    /// <summary>
    /// Combines two errors. If both are <see cref="Error.InvalidInput"/>, their
    /// <c>Fields</c> and <c>Rules</c> sequences are concatenated. Otherwise the two errors
    /// are flattened into an <see cref="Error.Aggregate"/> (any nested <see cref="Error.Aggregate"/>
    /// values are flattened by the constructor).
    /// </summary>
    /// <param name="thisError">The first error, or <see langword="null"/>.</param>
    /// <param name="otherError">The second error.</param>
    /// <returns>The combined error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="otherError"/> is null.</exception>
    public static Error Combine(this Error? thisError, Error otherError)
    {
        ArgumentNullException.ThrowIfNull(otherError);
        if (thisError is null) return otherError;

        if (thisError is Error.InvalidInput a && otherError is Error.InvalidInput b)
        {
            var mergedDetail = (a.Detail, b.Detail) switch
            {
                (null, null) => null,
                (null, var y) => y,
                (var x, null) => x,
                (var x, var y) when string.Equals(x, y, StringComparison.Ordinal) => x,
                (var x, var y) => $"{x}; {y}",
            };

            return new Error.InvalidInput(
                new EquatableArray<FieldViolation>(a.Fields.Items.AddRange(b.Fields.Items)),
                new EquatableArray<RuleViolation>(a.Rules.Items.AddRange(b.Rules.Items)))
            {
                Detail = mergedDetail,
                Cause = a.Cause ?? b.Cause,
            };
        }

        return new Error.Aggregate(EquatableArray.Create(thisError, otherError));
    }
}