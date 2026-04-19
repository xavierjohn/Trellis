namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on Result types.
/// </summary>
public static class ResultAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on Result.
    /// </summary>
    public static ResultAssertions<TValue> Should<TValue>(this Result<TValue> result) => new ResultAssertions<TValue>(result);
}

/// <summary>
/// Contains assertion methods for Result types.
/// </summary>
public class ResultAssertions<TValue> : ReferenceTypeAssertions<Result<TValue>, ResultAssertions<TValue>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResultAssertions{TValue}"/> class.
    /// </summary>
    public ResultAssertions(Result<TValue> result)
        : base(result)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "result";

    /// <summary>
    /// Asserts that the result is a success.
    /// </summary>
    /// <param name="because">
    /// A formatted phrase as is supported by <see cref="string.Format(string,object[])" /> explaining why the assertion
    /// is needed. If the phrase does not start with the word <i>because</i>, it is prepended automatically.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndWhichConstraint<ResultAssertions<TValue>, TValue> BeSuccess(
        string because = "",
        params object[] becauseArgs)
    {
        Subject.TryGetError(out var error);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.IsSuccess)
            .FailWith("Expected {context:result} to be success{reason}, but it failed with error: {0}",
                error);

        Subject.TryGetValue(out var value);
        return new AndWhichConstraint<ResultAssertions<TValue>, TValue>(this, value);
    }

    /// <summary>
    /// Asserts that the result is a failure.
    /// </summary>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndWhichConstraint<ResultAssertions<TValue>, Error> BeFailure(
        string because = "",
        params object[] becauseArgs)
    {
        Subject.TryGetValue(out var value);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.IsFailure)
            .FailWith("Expected {context:result} to be failure{reason}, but it succeeded with value: {0}",
                value);

        Subject.TryGetError(out var error);
        return new AndWhichConstraint<ResultAssertions<TValue>, Error>(this, error!);
    }

    /// <summary>
    /// Asserts that the result is a failure with a specific error type.
    /// </summary>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndWhichConstraint<ResultAssertions<TValue>, TError> BeFailureOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(error is TError)
            .FailWith("Expected {context:result} error to be of type {0}{reason}, but found {1}",
                FormatErrorTypeName(typeof(TError)),
                error is null ? null : FormatErrorTypeName(error.GetType()));

        return new AndWhichConstraint<ResultAssertions<TValue>, TError>(
            this,
            (TError)error!);
    }

    private static string FormatErrorTypeName(System.Type t)
    {
        var declaring = t.DeclaringType;
        return declaring is null ? t.Name : $"{declaring.Name}.{t.Name}";
    }

    /// <summary>
    /// Asserts that the success value equals the expected value.
    /// </summary>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveValue(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs)
    {
        BeSuccess(because, becauseArgs);

        Subject.TryGetValue(out var actualValue);
        actualValue.Should().Be(expectedValue, because, becauseArgs);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }

    /// <summary>
    /// Asserts that the success value satisfies a predicate.
    /// </summary>
    /// <param name="predicate">The predicate the value should satisfy.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveValueMatching(
        Func<TValue, bool> predicate,
        string because = "",
        params object[] becauseArgs)
    {
        BeSuccess(because, becauseArgs);

        Subject.TryGetValue(out var actualValue);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(predicate(actualValue))
            .FailWith("Expected {context:result} value to match predicate{reason}, but it did not. Value: {0}",
                actualValue);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }

    /// <summary>
    /// Asserts that the success value is equivalent to the expected value using structural comparison.
    /// </summary>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveValueEquivalentTo(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs)
    {
        BeSuccess(because, becauseArgs);

        Subject.TryGetValue(out var actualValue);
        actualValue.Should().BeEquivalentTo(expectedValue, because, becauseArgs);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }

    /// <summary>
    /// Asserts that the failure has a specific error code.
    /// </summary>
    /// <param name="expectedCode">The expected error code.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Code.Should().Be(expectedCode, because, becauseArgs);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }

    /// <summary>
    /// Asserts that the failure has a specific error detail.
    /// </summary>
    /// <param name="expectedDetail">The expected error detail.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Detail!.Should().Be(expectedDetail, because, becauseArgs);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }

    /// <summary>
    /// Asserts that the failure error detail contains the specified substring.
    /// </summary>
    /// <param name="substring">The substring to search for.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Detail!.Should().Contain(substring, because, becauseArgs);

        return new AndConstraint<ResultAssertions<TValue>>(this);
    }
}