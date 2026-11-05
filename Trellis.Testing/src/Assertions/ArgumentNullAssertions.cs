namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;

/// <summary>
/// Null-guard assertions that always name the offending parameter.
/// </summary>
public static class ArgumentNullAssertionsExtensions
{
    /// <summary>Asserts the action throws <see cref="ArgumentNullException"/> for exactly <paramref name="paramName"/>.</summary>
    /// <param name="act">The call under test.</param>
    /// <param name="paramName">The parameter that must be reported.</param>
    public static void ShouldRejectNull(this Action act, string paramName)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(paramName);
        act.Should().Throw<ArgumentNullException>().WithParameterName(paramName);
    }

    /// <summary>
    /// Asserts the value-returning call throws <see cref="ArgumentNullException"/> for exactly <paramref name="paramName"/>,
    /// so a guard test can pass <c>() =&gt; new Actor(...)</c> or a property getter without a discard.
    /// </summary>
    /// <typeparam name="T">The type the call returns; the value is discarded.</typeparam>
    /// <param name="act">The call under test.</param>
    /// <param name="paramName">The parameter that must be reported.</param>
    public static void ShouldRejectNull<T>(this Func<T> act, string paramName)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(paramName);
        ((Action)(() => act())).ShouldRejectNull(paramName);
    }

    /// <summary>Asserts the call throws or faults with <see cref="ArgumentNullException"/> for exactly <paramref name="paramName"/>.</summary>
    /// <param name="act">The call under test.</param>
    /// <param name="paramName">The parameter that must be reported.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldRejectNullAsync(this Func<Task> act, string paramName)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(paramName);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName(paramName).ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts the call rejects the null argument eagerly: it throws synchronously, or returns an already-faulted
    /// task, so a still-pending source was never awaited.
    /// </summary>
    /// <remarks>
    /// This form accepts a synchronous throw. To require that the error arrives through the returned task, start the
    /// operation yourself and use <see cref="ShouldRejectNullBeforeAwaitAsync(Task, string)"/>.
    /// </remarks>
    /// <param name="start">Starts the operation under test; its source should still be pending.</param>
    /// <param name="paramName">The parameter that must be reported.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldRejectNullBeforeAwaitAsync(this Func<Task> start, string paramName)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(paramName);

        Task operation;
        try
        {
            operation = start();
        }
        catch (ArgumentNullException thrown)
        {
            Execute.Assertion
                .ForCondition(thrown.ParamName == paramName)
                .FailWith("Expected ArgumentNullException for parameter {0}, but it named {1}.", paramName, thrown.ParamName);
            return;
        }

        if (!operation.IsCompleted)
        {
            Execute.Assertion.FailWith(
                "Expected the null argument {0} to be rejected before awaiting its source, but the operation was still pending.",
                paramName);
            return;
        }

        await ((Func<Task>)(() => operation)).ShouldRejectNullAsync(paramName).ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts an operation the caller already started rejected the null argument through its returned task, before
    /// awaiting a still-pending source.
    /// </summary>
    /// <remarks>
    /// Because the caller starts the operation, a synchronous throw escapes the test and fails it: use this form to
    /// require that the error is delivered through the awaitable, as the Trellis async extensions promise.
    /// </remarks>
    /// <param name="operation">The already-started operation; its source should still be pending.</param>
    /// <param name="paramName">The parameter that must be reported.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldRejectNullBeforeAwaitAsync(this Task operation, string paramName)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(paramName);

        if (!operation.IsCompleted)
        {
            Execute.Assertion.FailWith(
                "Expected the null argument {0} to be rejected before awaiting its source, but the operation was still pending.",
                paramName);
            return;
        }

        await ((Func<Task>)(() => operation)).ShouldRejectNullAsync(paramName).ConfigureAwait(false);
    }
}