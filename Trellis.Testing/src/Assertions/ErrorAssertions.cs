namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on Error types.
/// </summary>
public static class ErrorAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on Error.
    /// </summary>
    public static ErrorAssertions Should(this Error? error) => new ErrorAssertions(error!);
}

/// <summary>
/// Contains assertion methods for Error types.
/// </summary>
public class ErrorAssertions : ReferenceTypeAssertions<Error, ErrorAssertions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorAssertions"/> class.
    /// </summary>
    public ErrorAssertions(Error? error)
        : base(error!)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "error";

    private bool RequireNonNullSubject(string because, object[] becauseArgs) =>
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is not null)
            .FailWith("Expected {context:error} to not be <null>{reason}.");

    /// <summary>
    /// Asserts that the error equals the expected error (based on Error.Equals which compares by Code).
    /// </summary>
    /// <param name="expected">The expected error.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ErrorAssertions> Be(
        Error expected,
        string because = "",
        params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject!.Equals(expected))
                .FailWith("Expected {context:error} to be {0}{reason}, but found {1}",
                    expected, Subject);
        }

        return new AndConstraint<ErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error has the specified code.
    /// </summary>
    /// <param name="expectedCode">The expected error code.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ErrorAssertions> HaveCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
            Subject!.Code.Should().Be(expectedCode, because, becauseArgs);
        return new AndConstraint<ErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error has the specified detail message.
    /// </summary>
    /// <param name="expectedDetail">The expected error detail.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ErrorAssertions> HaveDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
            Subject!.Detail!.Should().Be(expectedDetail, because, becauseArgs);
        return new AndConstraint<ErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error detail contains the specified substring.
    /// </summary>
    /// <param name="substring">The substring to search for.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ErrorAssertions> HaveDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
            Subject!.Detail!.Should().Contain(substring, because, becauseArgs);
        return new AndConstraint<ErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error is of the specified type and returns a typed constraint for further assertions.
    /// </summary>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public new AndWhichConstraint<ErrorAssertions, TError> BeOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error
    {
        if (!RequireNonNullSubject(because, becauseArgs))
            return new AndWhichConstraint<ErrorAssertions, TError>(this, default(TError)!);

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is TError)
            .FailWith("Expected {context:error} to be of type {0}{reason}, but found {1}",
                typeof(TError).Name,
                Subject!.GetType().Name);

        return new AndWhichConstraint<ErrorAssertions, TError>(this, (TError)Subject!);
    }
}