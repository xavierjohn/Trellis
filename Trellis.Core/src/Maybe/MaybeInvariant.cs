namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides static methods for validating invariants across multiple <see cref="Maybe{T}"/> values.
/// These methods express relationships between correlated optional values — such as "all or none",
/// "if A then B", or "exactly one" — and return <see cref="Result{T}"/> with field-level
/// <see cref="Error.UnprocessableContent"/> details when the invariant is violated.
/// </summary>
/// <remarks>
/// <para>
/// Use these helpers in aggregate factory methods, command validation, and domain methods where
/// multiple optional values must satisfy a relationship constraint.
/// </para>
/// <para>
/// All methods return <see cref="Result"/>. Chain with <c>.Combine()</c> to compose
/// multiple invariant checks, or use as a step in a result pipeline.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Winner and winner points must both be present or both absent
/// var result = MaybeInvariant.AllOrNone(winner, winnerPoints, "winner", "winnerPoints");
///
/// // A discount requires a coupon code
/// var result = MaybeInvariant.Requires(discount, couponCode, "discount", "couponCode");
///
/// // Payment can be by card or bank transfer, but not both
/// var result = MaybeInvariant.MutuallyExclusive(cardPayment, bankTransfer, "cardPayment", "bankTransfer");
/// </code>
/// </example>
[DebuggerStepThrough]
public static class MaybeInvariant
{
    private const string ValidationErrorCode = "validation.error";

    #region AllOrNone

    /// <summary>
    /// Validates that all values are present or all values are absent.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="firstFieldName">Field name for the first value (used in validation errors).</param>
    /// <param name="secondFieldName">Field name for the second value (used in validation errors).</param>
    /// <returns>
    /// <see cref="Result"/> success if all values are present or all are absent;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing the fields that violate the invariant.
    /// </returns>
    public static Result AllOrNone<T1, T2>(
        Maybe<T1> first, Maybe<T2> second,
        string firstFieldName, string secondFieldName)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);

        return AllOrNoneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName));
    }

    /// <summary>
    /// Validates that all three values are present or all three are absent.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <typeparam name="T3">Type of the third optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="third">The third optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <param name="thirdFieldName">Field name for the third value.</param>
    /// <returns>
    /// <see cref="Result"/> success if all values are present or all are absent;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing the fields that violate the invariant.
    /// </returns>
    public static Result AllOrNone<T1, T2, T3>(
        Maybe<T1> first, Maybe<T2> second, Maybe<T3> third,
        string firstFieldName, string secondFieldName, string thirdFieldName)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);
        ArgumentNullException.ThrowIfNull(thirdFieldName);

        return AllOrNoneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName),
            (third.HasValue, thirdFieldName));
    }

    /// <summary>
    /// Validates that all four values are present or all four are absent.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <typeparam name="T3">Type of the third optional value.</typeparam>
    /// <typeparam name="T4">Type of the fourth optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="third">The third optional value.</param>
    /// <param name="fourth">The fourth optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <param name="thirdFieldName">Field name for the third value.</param>
    /// <param name="fourthFieldName">Field name for the fourth value.</param>
    /// <returns>
    /// <see cref="Result"/> success if all values are present or all are absent;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing the fields that violate the invariant.
    /// </returns>
    public static Result AllOrNone<T1, T2, T3, T4>(
        Maybe<T1> first, Maybe<T2> second, Maybe<T3> third, Maybe<T4> fourth,
        string firstFieldName, string secondFieldName, string thirdFieldName, string fourthFieldName)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        where T4 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);
        ArgumentNullException.ThrowIfNull(thirdFieldName);
        ArgumentNullException.ThrowIfNull(fourthFieldName);

        return AllOrNoneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName),
            (third.HasValue, thirdFieldName),
            (fourth.HasValue, fourthFieldName));
    }

    #endregion

    #region Requires

    /// <summary>
    /// Validates that if <paramref name="source"/> has a value, <paramref name="required"/> must also have a value.
    /// If <paramref name="source"/> has no value, the check passes regardless of <paramref name="required"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the source optional value.</typeparam>
    /// <typeparam name="T2">Type of the required optional value.</typeparam>
    /// <param name="source">The optional value that triggers the requirement.</param>
    /// <param name="required">The optional value that must be present when source is present.</param>
    /// <param name="sourceFieldName">Field name for the source value.</param>
    /// <param name="requiredFieldName">Field name for the required value.</param>
    /// <returns>
    /// <see cref="Result"/> success if source is absent or both are present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> for the missing required field.
    /// </returns>
    public static Result Requires<T1, T2>(
        Maybe<T1> source, Maybe<T2> required,
        string sourceFieldName, string requiredFieldName)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(sourceFieldName);
        ArgumentNullException.ThrowIfNull(requiredFieldName);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Requires));

        if (source.HasNoValue || required.HasValue)
            return Result.Ok();

        return Result.Fail(
            new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(requiredFieldName), ValidationErrorCode)
                {
                    Detail = $"'{requiredFieldName}' is required when '{sourceFieldName}' is provided.",
                })));
    }

    #endregion

    #region MutuallyExclusive

    /// <summary>
    /// Validates that at most one of the values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <returns>
    /// <see cref="Result"/> success if zero or one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all present fields.
    /// </returns>
    public static Result MutuallyExclusive<T1, T2>(
        Maybe<T1> first, Maybe<T2> second,
        string firstFieldName, string secondFieldName)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);

        return MutuallyExclusiveCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName));
    }

    /// <summary>
    /// Validates that at most one of the three values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <typeparam name="T3">Type of the third optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="third">The third optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <param name="thirdFieldName">Field name for the third value.</param>
    /// <returns>
    /// <see cref="Result"/> success if zero or one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all present fields.
    /// </returns>
    public static Result MutuallyExclusive<T1, T2, T3>(
        Maybe<T1> first, Maybe<T2> second, Maybe<T3> third,
        string firstFieldName, string secondFieldName, string thirdFieldName)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);
        ArgumentNullException.ThrowIfNull(thirdFieldName);

        return MutuallyExclusiveCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName),
            (third.HasValue, thirdFieldName));
    }

    #endregion

    #region ExactlyOne

    /// <summary>
    /// Validates that exactly one of the values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <returns>
    /// <see cref="Result"/> success if exactly one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all fields.
    /// </returns>
    public static Result ExactlyOne<T1, T2>(
        Maybe<T1> first, Maybe<T2> second,
        string firstFieldName, string secondFieldName)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);

        return ExactlyOneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName));
    }

    /// <summary>
    /// Validates that exactly one of the three values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <typeparam name="T3">Type of the third optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="third">The third optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <param name="thirdFieldName">Field name for the third value.</param>
    /// <returns>
    /// <see cref="Result"/> success if exactly one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all fields.
    /// </returns>
    public static Result ExactlyOne<T1, T2, T3>(
        Maybe<T1> first, Maybe<T2> second, Maybe<T3> third,
        string firstFieldName, string secondFieldName, string thirdFieldName)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);
        ArgumentNullException.ThrowIfNull(thirdFieldName);

        return ExactlyOneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName),
            (third.HasValue, thirdFieldName));
    }

    #endregion

    #region AtLeastOne

    /// <summary>
    /// Validates that at least one of the values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <returns>
    /// <see cref="Result"/> success if at least one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all fields.
    /// </returns>
    public static Result AtLeastOne<T1, T2>(
        Maybe<T1> first, Maybe<T2> second,
        string firstFieldName, string secondFieldName)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);

        return AtLeastOneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName));
    }

    /// <summary>
    /// Validates that at least one of the three values is present.
    /// </summary>
    /// <typeparam name="T1">Type of the first optional value.</typeparam>
    /// <typeparam name="T2">Type of the second optional value.</typeparam>
    /// <typeparam name="T3">Type of the third optional value.</typeparam>
    /// <param name="first">The first optional value.</param>
    /// <param name="second">The second optional value.</param>
    /// <param name="third">The third optional value.</param>
    /// <param name="firstFieldName">Field name for the first value.</param>
    /// <param name="secondFieldName">Field name for the second value.</param>
    /// <param name="thirdFieldName">Field name for the third value.</param>
    /// <returns>
    /// <see cref="Result"/> success if at least one value is present;
    /// otherwise a <see cref="Error.UnprocessableContent"/> listing all fields.
    /// </returns>
    public static Result AtLeastOne<T1, T2, T3>(
        Maybe<T1> first, Maybe<T2> second, Maybe<T3> third,
        string firstFieldName, string secondFieldName, string thirdFieldName)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        ArgumentNullException.ThrowIfNull(firstFieldName);
        ArgumentNullException.ThrowIfNull(secondFieldName);
        ArgumentNullException.ThrowIfNull(thirdFieldName);

        return AtLeastOneCore(
            (first.HasValue, firstFieldName),
            (second.HasValue, secondFieldName),
            (third.HasValue, thirdFieldName));
    }

    #endregion

    #region Core implementations

    private static Result AllOrNoneCore(params ReadOnlySpan<(bool hasValue, string fieldName)> fields)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(AllOrNone));

        int presentCount = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].hasValue)
                presentCount++;
        }

        if (presentCount == 0 || presentCount == fields.Length)
            return Result.Ok();

        // Some present, some absent — report the missing fields as required.
        var violations = new List<FieldViolation>();
        for (int i = 0; i < fields.Length; i++)
        {
            if (!fields[i].hasValue)
            {
                string message = $"'{fields[i].fieldName}' is required when related fields are provided.";
                violations.Add(new FieldViolation(InputPointer.ForProperty(fields[i].fieldName), ValidationErrorCode) { Detail = message });
            }
        }

        return Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.From(violations)));
    }

    private static Result MutuallyExclusiveCore(params ReadOnlySpan<(bool hasValue, string fieldName)> fields)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(MutuallyExclusive));

        int presentCount = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].hasValue)
                presentCount++;
        }

        if (presentCount <= 1)
            return Result.Ok();

        // Multiple present — report all present fields
        var violations = new List<FieldViolation>();
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].hasValue)
            {
                string message = $"'{fields[i].fieldName}' cannot be provided together with other mutually exclusive fields.";
                violations.Add(new FieldViolation(InputPointer.ForProperty(fields[i].fieldName), ValidationErrorCode) { Detail = message });
            }
        }

        return Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.From(violations)));
    }

    private static Result ExactlyOneCore(params ReadOnlySpan<(bool hasValue, string fieldName)> fields)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ExactlyOne));

        int presentCount = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].hasValue)
                presentCount++;
        }

        if (presentCount == 1)
            return Result.Ok();

        // Build error listing relevant fields
        var violations = new List<FieldViolation>();
        if (presentCount == 0)
        {
            // None present — report all fields
            for (int i = 0; i < fields.Length; i++)
            {
                violations.Add(new FieldViolation(InputPointer.ForProperty(fields[i].fieldName), ValidationErrorCode) { Detail = "Exactly one field must be provided." });
            }
        }
        else
        {
            // Multiple present — report only the fields that were provided
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].hasValue)
                {
                    violations.Add(new FieldViolation(InputPointer.ForProperty(fields[i].fieldName), ValidationErrorCode) { Detail = "Only one field may be provided." });
                }
            }
        }

        return Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.From(violations)));
    }

    private static Result AtLeastOneCore(params ReadOnlySpan<(bool hasValue, string fieldName)> fields)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(AtLeastOne));

        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].hasValue)
                return Result.Ok();
        }

        // None present — report all fields
        var violations = new List<FieldViolation>();
        const string message = "At least one of the related fields must be provided.";
        for (int i = 0; i < fields.Length; i++)
        {
            violations.Add(new FieldViolation(InputPointer.ForProperty(fields[i].fieldName), ValidationErrorCode) { Detail = message });
        }

        return Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.From(violations)));
    }

    #endregion
}