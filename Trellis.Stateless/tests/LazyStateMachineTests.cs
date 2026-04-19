namespace Trellis.Stateless.Tests;

using global::Stateless;

/// <summary>
/// Tests for <see cref="LazyStateMachine{TState, TTrigger}"/>.
/// Covers lazy initialization, FireResult delegation, EF Core materialization scenarios,
/// and argument validation.
/// </summary>
public class LazyStateMachineTests
{
    private enum State { Draft, Submitted, Approved, Shipped, Cancelled }
    private enum Trigger { Submit, Approve, Ship, Cancel }

    #region Lazy initialization

    [Fact]
    public void Constructor_DoesNotInvokeStateAccessor()
    {
        var accessorInvoked = false;

        _ = new LazyStateMachine<State, Trigger>(
            () => { accessorInvoked = true; return State.Draft; },
            _ => { },
            _ => { });

        accessorInvoked.Should().BeFalse("state accessor must not be invoked during construction");
    }

    [Fact]
    public void Constructor_DoesNotInvokeConfigure()
    {
        var configureInvoked = false;

        _ = new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            _ => { },
            _ => configureInvoked = true);

        configureInvoked.Should().BeFalse("configure must not be invoked during construction");
    }

    [Fact]
    public void Machine_InvokesConfigureOnFirstAccess()
    {
        var configureInvoked = false;
        var lazy = new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            _ => { },
            _ => configureInvoked = true);

        _ = lazy.Machine;

        configureInvoked.Should().BeTrue();
    }

    [Fact]
    public void Machine_InvokesConfigureOnlyOnce()
    {
        var configureCount = 0;
        var lazy = new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            _ => { },
            _ => configureCount++);

        _ = lazy.Machine;
        _ = lazy.Machine;
        _ = lazy.Machine;

        configureCount.Should().Be(1);
    }

    [Fact]
    public void Machine_ReturnsSameInstanceOnSubsequentAccesses()
    {
        var lazy = new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            _ => { },
            _ => { });

        var first = lazy.Machine;
        var second = lazy.Machine;

        first.Should().BeSameAs(second);
    }

    #endregion

    #region FireResult delegation

    [Fact]
    public void FireResult_ValidTransition_ReturnsSuccessWithNewState()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        var result = lazy.FireResult(Trigger.Submit);

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var v).Should().BeTrue();
        v.Should().Be(State.Submitted);
        status.Should().Be(State.Submitted);
    }

    [Fact]
    public void FireResult_InvalidTransition_ReturnsFailureWithDomainError()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        var result = lazy.FireResult(Trigger.Ship);

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var err).Should().BeTrue();
        err.Should().BeOfType<DomainError>();
        err.Code.Should().Be("state.machine.invalid.transition");
    }

    [Fact]
    public void FireResult_InvalidTransition_DoesNotChangeState()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        _ = lazy.FireResult(Trigger.Ship);

        status.Should().Be(State.Draft);
    }

    [Fact]
    public void FireResult_MultipleTransitions_TracksStateCorrectly()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        lazy.FireResult(Trigger.Submit).IsSuccess.Should().BeTrue();
        status.Should().Be(State.Submitted);

        lazy.FireResult(Trigger.Approve).IsSuccess.Should().BeTrue();
        status.Should().Be(State.Approved);

        lazy.FireResult(Trigger.Ship).IsSuccess.Should().BeTrue();
        status.Should().Be(State.Shipped);
    }

    [Fact]
    public void FireResult_InitializesMachineOnFirstCall()
    {
        var configureInvoked = false;
        var status = State.Draft;
        var lazy = new LazyStateMachine<State, Trigger>(
            () => status,
            s => status = s,
            machine =>
            {
                configureInvoked = true;
                ConfigureOrderMachine(machine);
            });

        configureInvoked.Should().BeFalse();

        _ = lazy.FireResult(Trigger.Submit);

        configureInvoked.Should().BeTrue();
    }

    #endregion

    #region EF Core materialization scenario

    [Fact]
    public void EfCoreMaterialization_StateAccessorNotInvokedDuringConstruction()
    {
        // Simulate EF Core materialization: status is default/null during construction
        State? status = null;

        // This should NOT throw — accessor is deferred
        var lazy = new LazyStateMachine<State, Trigger>(
            () => status!.Value,
            s => status = s,
            ConfigureOrderMachine);

        // EF Core populates the property after construction
        status = State.Draft;

        // Now it's safe to use
        var result = lazy.FireResult(Trigger.Submit);
        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var v).Should().BeTrue();
        v.Should().Be(State.Submitted);
    }

    #endregion

    #region Guarded transitions

    [Fact]
    public void FireResult_GuardedTransition_GuardTrue_ReturnsSuccess()
    {
        var isReady = true;
        var status = State.Draft;
        var lazy = new LazyStateMachine<State, Trigger>(
            () => status,
            s => status = s,
            machine => machine.Configure(State.Draft)
                .PermitIf(Trigger.Submit, State.Submitted, () => isReady));

        var result = lazy.FireResult(Trigger.Submit);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FireResult_GuardedTransition_GuardFalse_ReturnsFailure()
    {
        var isReady = false;
        var status = State.Draft;
        var lazy = new LazyStateMachine<State, Trigger>(
            () => status,
            s => status = s,
            machine => machine.Configure(State.Draft)
                .PermitIf(Trigger.Submit, State.Submitted, () => isReady));

        var result = lazy.FireResult(Trigger.Submit);

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var err).Should().BeTrue();
        err.Should().BeOfType<DomainError>();
    }

    #endregion

    #region String state machine

    [Fact]
    public void FireResult_StringStates_ValidTransition_ReturnsSuccess()
    {
        var status = "Draft";
        var lazy = new LazyStateMachine<string, string>(
            () => status,
            s => status = s,
            machine => machine.Configure("Draft").Permit("Publish", "Published"));

        var result = lazy.FireResult("Publish");

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var v).Should().BeTrue();
        v.Should().Be("Published");
        status.Should().Be("Published");
    }

    [Fact]
    public void FireResult_StringStates_InvalidTransition_ReturnsFailure()
    {
        var status = "Draft";
        var lazy = new LazyStateMachine<string, string>(
            () => status,
            s => status = s,
            machine => machine.Configure("Draft").Permit("Publish", "Published"));

        var result = lazy.FireResult("Archive");

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var err).Should().BeTrue();
        err.Should().BeOfType<DomainError>();
    }

    #endregion

    #region Argument validation

    [Fact]
    public void Constructor_NullStateAccessor_ThrowsArgumentNullException()
    {
        var act = () => new LazyStateMachine<State, Trigger>(
            null!,
            _ => { },
            _ => { });

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("stateAccessor");
    }

    [Fact]
    public void Constructor_NullStateMutator_ThrowsArgumentNullException()
    {
        var act = () => new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            null!,
            _ => { });

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("stateMutator");
    }

    [Fact]
    public void Constructor_NullConfigure_ThrowsArgumentNullException()
    {
        var act = () => new LazyStateMachine<State, Trigger>(
            () => State.Draft,
            _ => { },
            null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("configure");
    }

    #endregion

    #region Machine property

    [Fact]
    public void Machine_ReturnsConfiguredStateMachine()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        var machine = lazy.Machine;

        machine.Should().NotBeNull();
        machine.State.Should().Be(State.Draft);
        machine.CanFire(Trigger.Submit).Should().BeTrue();
        machine.CanFire(Trigger.Ship).Should().BeFalse();
    }

    [Fact]
    public void Machine_CanBeUsedDirectlyForCanFire()
    {
        var status = State.Draft;
        var lazy = CreateLazyMachine(() => status, s => status = s);

        lazy.Machine.CanFire(Trigger.Submit).Should().BeTrue();
        lazy.Machine.CanFire(Trigger.Ship).Should().BeFalse();
    }

    #endregion

    private static LazyStateMachine<State, Trigger> CreateLazyMachine(
        Func<State> stateAccessor,
        Action<State> stateMutator) =>
        new(stateAccessor, stateMutator, ConfigureOrderMachine);

    private static void ConfigureOrderMachine(StateMachine<State, Trigger> machine)
    {
        machine.Configure(State.Draft)
            .Permit(Trigger.Submit, State.Submitted)
            .Permit(Trigger.Cancel, State.Cancelled);

        machine.Configure(State.Submitted)
            .Permit(Trigger.Approve, State.Approved)
            .Permit(Trigger.Cancel, State.Cancelled);

        machine.Configure(State.Approved)
            .Permit(Trigger.Ship, State.Shipped)
            .Permit(Trigger.Cancel, State.Cancelled);
    }
}