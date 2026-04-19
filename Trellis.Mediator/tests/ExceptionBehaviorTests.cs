using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.Logging;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ExceptionBehavior{TMessage, TResponse}"/>.
/// </summary>
public class ExceptionBehaviorTests
{
    #region Handler throws — catches and returns failure

    [Fact]
    public async Task Handle_HandlerThrows_ReturnsUnexpectedError()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new InvalidOperationException("Something went wrong"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().NotContain("Something went wrong");
        result.UnwrapError().Detail.Should().Be("An unexpected error occurred while processing the request.");
    }

    [Fact]
    public async Task Handle_HandlerThrows_DoesNotLeakExceptionMessage()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new InvalidOperationException("Connection string: Server=prod-db;Password=s3cret"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().NotContain("s3cret",
            "exception messages may contain credentials and must not be exposed in error details");
        result.UnwrapError().Detail.Should().NotContain("Connection string",
            "internal infrastructure details must not be exposed in error details");
    }

    #endregion

    #region Handler throws — logs at Error level

    [Fact]
    public async Task Handle_HandlerThrows_LogsAtErrorLevel()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new InvalidOperationException("Something went wrong"));

        await behavior.Handle(command, next, CancellationToken.None);

        logEntries.Should().ContainSingle();
        logEntries[0].Level.Should().Be(LogLevel.Error);
        logEntries[0].Ex.Should().BeOfType<InvalidOperationException>();
        logEntries[0].Message.Should().Contain("TestCommand");
    }

    #endregion

    #region OperationCanceledException — NOT caught

    [Fact]
    public async Task Handle_OperationCanceled_RethrowsException()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new OperationCanceledException("Canceled"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logEntries.Should().BeEmpty("OperationCanceledException should not be logged");
    }

    [Fact]
    public async Task Handle_TaskCanceledException_RethrowsException()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new TaskCanceledException("Canceled"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    #endregion

    #region Handler succeeds — passes through unchanged

    [Fact]
    public async Task Handle_HandlerSucceeds_ReturnsResultUnchanged()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Ok("Hello, Alice!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Hello, Alice!");
        logEntries.Should().BeEmpty("no exception means no logging");
    }

    [Fact]
    public async Task Handle_HandlerReturnsFailure_PassesThroughUnchanged()
    {
        var logEntries = new List<(LogLevel Level, string Message, Exception? Ex)>();
        var logger = new FakeLogger<ExceptionBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new ExceptionBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Business rule failed." }));
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(error));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Be("Business rule failed.");
        logEntries.Should().BeEmpty("failure results are not exceptions");
    }

    #endregion

    /// <summary>
    /// Minimal fake logger that captures log entries with exceptions for assertion.
    /// </summary>
    private sealed class FakeLogger<T>(List<(LogLevel Level, string Message, Exception? Ex)> entries) : ILogger<T>
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, formatter(state, exception), exception));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
