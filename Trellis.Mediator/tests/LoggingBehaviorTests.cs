namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.Logging;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="LoggingBehavior{TMessage, TResponse}"/>.
/// </summary>
public class LoggingBehaviorTests
{
    #region Successful handler — logs at Information level

    [Fact]
    public async Task Handle_SuccessfulResult_LogsAtInformationLevel()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Ok("Hello, Alice!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        logEntries.Should().HaveCount(2);
        logEntries[0].Level.Should().Be(LogLevel.Information);
        logEntries[0].Message.Should().Contain("TestCommand");
        logEntries[1].Level.Should().Be(LogLevel.Information);
        logEntries[1].Message.Should().Contain("TestCommand");
        logEntries[1].Message.Should().Contain("ms");
    }

    #endregion

    #region Failed Result — logs at Warning level

    [Fact]
    public async Task Handle_FailedResult_LogsAtWarningLevel()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(Error.Validation("Something failed.", "field")));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logEntries.Should().HaveCount(2);
        logEntries[0].Level.Should().Be(LogLevel.Information);
        logEntries[1].Level.Should().Be(LogLevel.Warning);
        logEntries[1].Message.Should().Contain("TestCommand");
        logEntries[1].Message.Should().Contain("Something failed.");
    }

    #endregion

    #region Structured log properties

    [Fact]
    public async Task Handle_Success_LogsMessageNameAndElapsedMs()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<AdminCommand, Result<string>>>(logEntries);
        var behavior = new LoggingBehavior<AdminCommand, Result<string>>(logger);
        var command = new AdminCommand("data");
        var next = NextDelegate.ReturningAsync<AdminCommand, Result<string>>(
            Result.Ok("Done"));

        await behavior.Handle(command, next, CancellationToken.None);

        logEntries[0].Message.Should().Contain("AdminCommand");
        logEntries[1].Message.Should().Contain("AdminCommand");
    }

    #endregion

    /// <summary>
    /// Minimal fake logger that captures log entries for assertion.
    /// </summary>
    private sealed class FakeLogger<T>(List<(LogLevel Level, string Message)> entries) : ILogger<T>
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}