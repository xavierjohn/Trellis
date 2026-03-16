namespace Trellis.Stateless.Tests;

using global::Stateless;

/// <summary>
/// Tests for <see cref="StateMachineExtensions.FireResult{TState, TTrigger}"/>.
/// Covers valid transitions, invalid transitions, and guarded transitions.
/// </summary>
public class StateMachineExtensionsTests
{
    private enum State { Idle, Running, Paused, Completed }
    private enum Trigger { Start, Pause, Resume, Complete, Reset }

    #region Valid transitions

    [Fact]
    public void FireResult_ValidTransition_ReturnsSuccessWithNewState()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);

        // Act
        var result = machine.FireResult(Trigger.Start);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(State.Running);
    }

    [Fact]
    public void FireResult_MultipleValidTransitions_ReturnsSuccessEachTime()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);
        machine.Configure(State.Running).Permit(Trigger.Pause, State.Paused);
        machine.Configure(State.Paused).Permit(Trigger.Resume, State.Running);

        // Act & Assert
        var result1 = machine.FireResult(Trigger.Start);
        result1.IsSuccess.Should().BeTrue();
        result1.Value.Should().Be(State.Running);

        var result2 = machine.FireResult(Trigger.Pause);
        result2.IsSuccess.Should().BeTrue();
        result2.Value.Should().Be(State.Paused);

        var result3 = machine.FireResult(Trigger.Resume);
        result3.IsSuccess.Should().BeTrue();
        result3.Value.Should().Be(State.Running);
    }

    [Fact]
    public void FireResult_SelfTransition_ReturnsSuccessWithSameState()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Running);
        machine.Configure(State.Running).PermitReentry(Trigger.Reset);

        // Act
        var result = machine.FireResult(Trigger.Reset);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(State.Running);
    }

    #endregion

    #region Invalid transitions

    [Fact]
    public void FireResult_InvalidTransition_ReturnsFailureWithDomainError()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);

        // Act
        var result = machine.FireResult(Trigger.Pause);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DomainError>();
        result.Error.Detail.Should().Contain("Pause");
        result.Error.Detail.Should().Contain("Idle");
    }

    [Fact]
    public void FireResult_InvalidTransition_DoesNotChangeState()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);

        // Act
        _ = machine.FireResult(Trigger.Pause);

        // Assert
        machine.State.Should().Be(State.Idle);
    }

    [Fact]
    public void FireResult_InvalidTransition_ErrorCodeIsStateMachineInvalidTransition()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);

        // Act
        var result = machine.FireResult(Trigger.Complete);

        // Assert
        result.Error.Code.Should().Be("state.machine.invalid.transition");
    }

    [Fact]
    public void FireResult_NoTransitionsConfigured_ReturnsFailure()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);

        // Act
        var result = machine.FireResult(Trigger.Start);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DomainError>();
    }

    #endregion

    #region Guarded transitions

    [Fact]
    public void FireResult_GuardedTransition_GuardTrue_ReturnsSuccess()
    {
        // Arrange
        var isReady = true;
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle)
            .PermitIf(Trigger.Start, State.Running, () => isReady);

        // Act
        var result = machine.FireResult(Trigger.Start);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(State.Running);
    }

    [Fact]
    public void FireResult_GuardedTransition_GuardFalse_ReturnsFailure()
    {
        // Arrange
        var isReady = false;
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle)
            .PermitIf(Trigger.Start, State.Running, () => isReady);

        // Act
        var result = machine.FireResult(Trigger.Start);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DomainError>();
        result.Error.Detail.Should().Contain("Start");
        result.Error.Detail.Should().Contain("Idle");
    }

    [Fact]
    public void FireResult_GuardedTransition_GuardFalse_DoesNotChangeState()
    {
        // Arrange
        var isReady = false;
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle)
            .PermitIf(Trigger.Start, State.Running, () => isReady);

        // Act
        _ = machine.FireResult(Trigger.Start);

        // Assert
        machine.State.Should().Be(State.Idle);
    }

    [Fact]
    public void FireResult_GuardedTransition_GuardChangesAtRuntime_RespectsCurrentValue()
    {
        // Arrange
        var isReady = false;
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle)
            .PermitIf(Trigger.Start, State.Running, () => isReady);

        // Act — guard is false
        var result1 = machine.FireResult(Trigger.Start);
        result1.IsFailure.Should().BeTrue();

        // Change guard at runtime
        isReady = true;

        // Act — guard is now true
        var result2 = machine.FireResult(Trigger.Start);
        result2.IsSuccess.Should().BeTrue();
        result2.Value.Should().Be(State.Running);
    }

    [Fact]
    public void FireResult_GuardedTransition_EvaluatesGuardOnlyOnce()
    {
        // Arrange
        var guardCallCount = 0;
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle)
            .PermitIf(Trigger.Start, State.Running, () =>
            {
                guardCallCount++;
                return true;
            });

        // Act
        var result = machine.FireResult(Trigger.Start);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(State.Running);
        guardCallCount.Should().Be(1, "FireResult should not evaluate transition guards more than once");
    }

    [Fact]
    public void FireResult_EntryActionThrowsInvalidOperationException_PropagatesException()
    {
        // Arrange
        var machine = new StateMachine<State, Trigger>(State.Idle);
        machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);
        machine.Configure(State.Running)
            .OnEntry(() => throw new InvalidOperationException("boom"));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => machine.FireResult(Trigger.Start));

        // Assert
        exception.Message.Should().Be("boom");
    }

    #endregion

    #region String state machine

    [Fact]
    public void FireResult_StringStates_ValidTransition_ReturnsSuccess()
    {
        // Arrange
        var machine = new StateMachine<string, string>("Draft");
        machine.Configure("Draft").Permit("Publish", "Published");

        // Act
        var result = machine.FireResult("Publish");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Published");
    }

    [Fact]
    public void FireResult_StringStates_InvalidTransition_ReturnsFailure()
    {
        // Arrange
        var machine = new StateMachine<string, string>("Draft");
        machine.Configure("Draft").Permit("Publish", "Published");

        // Act
        var result = machine.FireResult("Archive");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DomainError>();
    }

    #endregion
}