namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on the non-generic <see cref="Result"/>.
/// </summary>
public static class NonGenericResultAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on a non-generic <see cref="Result"/>.
    /// </summary>
    public static NonGenericResultAssertions Should(this Result result) => new(result);
}

/// <summary>
/// Contains assertion methods for the non-generic <see cref="Result"/> type.
/// </summary>
public class NonGenericResultAssertions : ReferenceTypeAssertions<Result, NonGenericResultAssertions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NonGenericResultAssertions"/> class.
    /// </summary>
    public NonGenericResultAssertions(Result result)
        : base(result)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "result";

    /// <summary>
    /// Asserts that the result is a success.
    /// </summary>
    public AndConstraint<NonGenericResultAssertions> BeSuccess(
        string because = "",
        params object[] becauseArgs)
    {
        Subject.TryGetError(out var error);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.IsSuccess)
            .FailWith("Expected {context:result} to be success{reason}, but it failed with error: {0}",
                error);

        return new AndConstraint<NonGenericResultAssertions>(this);
    }

    /// <summary>
    /// Asserts that the result is a failure.
    /// </summary>
    public AndWhichConstraint<NonGenericResultAssertions, Error> BeFailure(
        string because = "",
        params object[] becauseArgs)
    {
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.IsFailure)
            .FailWith("Expected {context:result} to be failure{reason}, but it succeeded.");

        Subject.TryGetError(out var error);
        return new AndWhichConstraint<NonGenericResultAssertions, Error>(this, error!);
    }

    /// <summary>
    /// Asserts that the result is a failure with a specific error type.
    /// </summary>
    public AndWhichConstraint<NonGenericResultAssertions, TError> BeFailureOfType<TError>(
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

        return new AndWhichConstraint<NonGenericResultAssertions, TError>(
            this,
            (TError)error!);
    }

    private static string FormatErrorTypeName(System.Type t)
    {
        var declaring = t.DeclaringType;
        return declaring is null ? t.Name : $"{declaring.Name}.{t.Name}";
    }

    /// <summary>
    /// Asserts that the failure has a specific error code.
    /// </summary>
    public AndConstraint<NonGenericResultAssertions> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Code.Should().Be(expectedCode, because, becauseArgs);

        return new AndConstraint<NonGenericResultAssertions>(this);
    }

    /// <summary>
    /// Asserts that the failure has a specific error detail.
    /// </summary>
    public AndConstraint<NonGenericResultAssertions> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Detail!.Should().Be(expectedDetail, because, becauseArgs);

        return new AndConstraint<NonGenericResultAssertions>(this);
    }

    /// <summary>
    /// Asserts that the failure error detail contains the specified substring.
    /// </summary>
    public AndConstraint<NonGenericResultAssertions> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs)
    {
        BeFailure(because, becauseArgs);

        Subject.TryGetError(out var error);
        error!.Detail!.Should().Contain(substring, because, becauseArgs);

        return new AndConstraint<NonGenericResultAssertions>(this);
    }
}