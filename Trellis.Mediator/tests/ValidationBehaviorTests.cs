using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ValidationBehavior{TMessage, TResponse}"/>.
/// </summary>
public class ValidationBehaviorTests
{
    #region Valid message — handler is called

    [Fact]
    public async Task Handle_ValidMessage_CallsNextAndReturnsHandlerResult()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("Hello, Alice!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Hello, Alice!");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Invalid message — handler is NOT called

    [Fact]
    public async Task Handle_InvalidMessage_DoesNotCallNextAndReturnsFailure()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("   ");
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        result.UnwrapError().Detail.Should().Contain("Name");
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked for invalid messages");
    }

    [Fact]
    public async Task Handle_NullName_ReturnsValidationFailure()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>();
        var command = new TestCommand(null!);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Validation with query

    [Fact]
    public async Task Handle_ValidQuery_CallsNextAndReturnsHandlerResult()
    {
        var behavior = new ValidationBehavior<TestQuery, Result<string>>();
        var query = new TestQuery(42);
        var (next, tracker) = NextDelegate.TrackingAsync<TestQuery, Result<string>>(
            Result.Ok("Result-42"));

        var result = await behavior.Handle(query, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Result-42");
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidQuery_ReturnsValidationFailure()
    {
        var behavior = new ValidationBehavior<TestQuery, Result<string>>();
        var query = new TestQuery(-1);
        var (next, tracker) = NextDelegate.TrackingAsync<TestQuery, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(query, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("Id");
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion
}
