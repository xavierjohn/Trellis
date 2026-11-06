namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>Extension methods to enable FluentAssertions on <see cref="CallbackRecorder"/> and <see cref="CallbackRecorder{TArgument}"/>.</summary>
public static class CallbackRecorderAssertionsExtensions
{
    /// <summary>Returns call-count assertions for a parameterless recorder.</summary>
    /// <param name="recorder">The recorder under test.</param>
    /// <returns>Assertions for the recorder.</returns>
    public static CallbackRecorderAssertions Should(this CallbackRecorder recorder) => new(recorder);

    /// <summary>Returns call-count assertions for an argument recorder.</summary>
    /// <typeparam name="TArgument">The recorded argument type.</typeparam>
    /// <param name="recorder">The recorder under test.</param>
    /// <returns>Assertions for the recorder.</returns>
    public static CallbackRecorderAssertions<TArgument> Should<TArgument>(this CallbackRecorder<TArgument> recorder) => new(recorder);
}

/// <summary>Call-count assertions for a <see cref="CallbackRecorder"/>.</summary>
public class CallbackRecorderAssertions : ReferenceTypeAssertions<CallbackRecorder, CallbackRecorderAssertions>
{
    /// <summary>Initializes a new instance of the <see cref="CallbackRecorderAssertions"/> class.</summary>
    /// <param name="subject">The recorder under test.</param>
    public CallbackRecorderAssertions(CallbackRecorder subject)
        : base(subject)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "callback recorder";

    /// <summary>Asserts the callback was never entered.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions> NotHaveBeenCalled(string because = "", params object[] becauseArgs) =>
        Verify(count => count == 0, "not to have been called", because, becauseArgs);

    /// <summary>Asserts the callback was entered exactly once.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions> HaveBeenCalledOnce(string because = "", params object[] becauseArgs) =>
        Verify(count => count == 1, "to have been called once", because, becauseArgs);

    /// <summary>Asserts the callback was entered at least once.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions> HaveBeenCalledAtLeastOnce(string because = "", params object[] becauseArgs) =>
        Verify(count => count >= 1, "to have been called at least once", because, becauseArgs);

    /// <summary>Asserts the callback was entered exactly <paramref name="expected"/> times.</summary>
    /// <param name="expected">The expected number of entries.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    public AndConstraint<CallbackRecorderAssertions> HaveBeenCalledTimes(int expected, string because = "", params object[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        return Verify(count => count == expected, $"to have been called {expected} time(s)", because, becauseArgs);
    }

    private AndConstraint<CallbackRecorderAssertions> Verify(Func<int, bool> isExpected, string expectation, string because, object[] becauseArgs)
    {
        CallCountVerification.Verify(Subject is null ? null : Subject.CallCount, isExpected, expectation, null, because, becauseArgs);
        return new AndConstraint<CallbackRecorderAssertions>(this);
    }
}

/// <summary>Call-count assertions for a <see cref="CallbackRecorder{TArgument}"/>.</summary>
/// <typeparam name="TArgument">The recorded argument type.</typeparam>
public class CallbackRecorderAssertions<TArgument> : ReferenceTypeAssertions<CallbackRecorder<TArgument>, CallbackRecorderAssertions<TArgument>>
{
    /// <summary>Initializes a new instance of the <see cref="CallbackRecorderAssertions{TArgument}"/> class.</summary>
    /// <param name="subject">The recorder under test.</param>
    public CallbackRecorderAssertions(CallbackRecorder<TArgument> subject)
        : base(subject)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "callback recorder";

    /// <summary>Asserts the callback was never entered.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions<TArgument>> NotHaveBeenCalled(string because = "", params object[] becauseArgs) =>
        Verify(count => count == 0, "not to have been called", because, becauseArgs);

    /// <summary>Asserts the callback was entered exactly once.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions<TArgument>> HaveBeenCalledOnce(string because = "", params object[] becauseArgs) =>
        Verify(count => count == 1, "to have been called once", because, becauseArgs);

    /// <summary>Asserts the callback was entered at least once.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<CallbackRecorderAssertions<TArgument>> HaveBeenCalledAtLeastOnce(string because = "", params object[] becauseArgs) =>
        Verify(count => count >= 1, "to have been called at least once", because, becauseArgs);

    /// <summary>Asserts the callback was entered exactly <paramref name="expected"/> times.</summary>
    /// <param name="expected">The expected number of entries.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expected"/> is negative.</exception>
    public AndConstraint<CallbackRecorderAssertions<TArgument>> HaveBeenCalledTimes(int expected, string because = "", params object[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        return Verify(count => count == expected, $"to have been called {expected} time(s)", because, becauseArgs);
    }

    private AndConstraint<CallbackRecorderAssertions<TArgument>> Verify(Func<int, bool> isExpected, string expectation, string because, object[] becauseArgs)
    {
        var recorded = Subject is null ? null : Subject.Arguments;
        CallCountVerification.Verify(recorded?.Count, isExpected, expectation, recorded, because, becauseArgs);
        return new AndConstraint<CallbackRecorderAssertions<TArgument>>(this);
    }
}

internal static class CallCountVerification
{
    internal static void Verify(int? callCount, Func<int, bool> isExpected, string expectation, System.Collections.IEnumerable? recordedArguments, string because, object[] becauseArgs)
    {
        var present = Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(callCount is not null)
            .FailWith("Expected {context:callback recorder} to not be <null>{reason}.");
        if (!present)
            return;

        var count = callCount!.Value;
        var assertion = Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(isExpected(count));
        if (recordedArguments is null)
            assertion.FailWith("Expected {context:callback recorder} " + expectation + "{reason}, but it was called {0} time(s).", count);
        else
            assertion.FailWith("Expected {context:callback recorder} " + expectation + "{reason}, but it was called {0} time(s) with {1}.", count, recordedArguments);
    }
}
