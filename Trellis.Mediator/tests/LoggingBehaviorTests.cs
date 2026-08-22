namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.Logging;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="LoggingBehavior{TMessage, TResponse}"/>.
/// </summary>
public class LoggingBehaviorTests
{
    public static TheoryData<Error, LogLevel> FailureLogLevelCases => new()
    {
        { Error.InvalidInput.ForField("field", ValidationCodes.Unspecified, "Something failed."), LogLevel.Information },
        { new Error.AuthenticationRequired(), LogLevel.Information },
        { new Error.Forbidden("authorization.insufficient.permissions"), LogLevel.Information },
        { new Error.Unexpected("unhandled-exception"), LogLevel.Warning },
    };

    #region Successful handler — logs at Debug level

    [Fact]
    public async Task Handle_SuccessfulResult_LogsAtDebugLevel()
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
        // Debug, not Information: cross-cutting per-call timing should not flood production
        // logs at the default minimum level. Consumers who want it opt in via
        // "Trellis.Mediator": "Debug" in appsettings.
        logEntries[0].Level.Should().Be(LogLevel.Debug);
        logEntries[0].Message.Should().Contain("TestCommand");
        logEntries[1].Level.Should().Be(LogLevel.Debug);
        logEntries[1].Message.Should().Contain("TestCommand");
        logEntries[1].Message.Should().Contain("ms");
    }

    #endregion

    #region Failed Result — start at Debug, failure exit at classified level

    [Theory]
    [MemberData(nameof(FailureLogLevelCases))]
    public async Task Handle_FailedResult_ErrorKind_LogsAtExpectedLevel(Error error, LogLevel expectedLevel)
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(error));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logEntries.Should().HaveCount(2);
        logEntries[0].Level.Should().Be(LogLevel.Debug);
        logEntries[1].Level.Should().Be(expectedLevel);
        logEntries[1].Message.Should().Contain("TestCommand");
        // ga-12: Detail is redacted by default (it can contain user input/PII). Only the
        // wire code and the error type are emitted unless
        // TrellisMediatorTelemetryOptions.IncludeErrorDetail is opted in.
        logEntries[1].Message.Should().Contain(error.Code);
        logEntries[1].Message.Should().NotContain("Something failed.");
    }

    [Fact]
    public async Task Handle_FailedResult_RedactsErrorDetailByDefault()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<TestCommand, Result<string>>>(logEntries);
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(logger);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(new Error.NotFound(new ResourceRef("Order", "42")) { Detail = "order 42 for tenant acme" }));

        await behavior.Handle(command, next, CancellationToken.None);

        logEntries[1].Message.Should().Contain("Error.NotFound",
            "the type keeps the case identifiable when the wire code is the sentinel");
        logEntries[1].Message.Should().NotContain("order 42 for tenant acme",
            "Error.Detail can carry user input or PII and must not leak into logs by default");
    }

    [Fact]
    public async Task Handle_FailedResult_IncludesErrorDetailWhenOptedIn()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var logger = new FakeLogger<LoggingBehavior<TestCommand, Result<string>>>(logEntries);
        var options = new TrellisMediatorTelemetryOptions { IncludeErrorDetail = true };
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(logger, options);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(new Error.NotFound(new ResourceRef("Order", "42")) { Detail = "order 42 for tenant acme" }));

        await behavior.Handle(command, next, CancellationToken.None);

        logEntries[1].Message.Should().Contain("order 42 for tenant acme",
            "operators may explicitly opt in to including Error.Detail in log output");
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