namespace Trellis.StateMachine.Tests;

using global::Stateless;

public class OrderWorkflowTests
{
    [Fact]
    public void Submit_AllowedTransition_ReturnsSuccessAndUpdatesState()
    {
        var order = new Order(canSubmit: true);

        var result = order.Submit();

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var state).Should().BeTrue();
        state.Should().Be(OrderState.Submitted);
        order.State.Should().Be(OrderState.Submitted);
    }

    [Fact]
    public void Submit_BlockedTransition_ReturnsFailureAndLeavesStateUnchanged()
    {
        var order = new Order(canSubmit: false);
        Result<OrderState>? result = null;

        var act = () => result = order.Submit();

        act.Should().NotThrow("FireResult converts blocked transitions to Result failures");
        result!.Value.IsFailure.Should().BeTrue();
        result.Value.TryGetError(out var error).Should().BeTrue();
        var unprocessable = error!.Should().BeOfType<Error.UnprocessableContent>().Subject;
        unprocessable.Rules.Items.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("state.machine.invalid.transition");
        order.State.Should().Be(OrderState.Draft);
    }

    private sealed class Order(bool canSubmit)
    {
        public OrderState State { get; private set; } = OrderState.Draft;

        public Result<OrderState> Submit()
        {
            var machine = new StateMachine<OrderState, OrderTrigger>(State);
            machine.Configure(OrderState.Draft)
                .PermitIf(OrderTrigger.Submit, OrderState.Submitted, () => canSubmit);

            Result<OrderState> result = machine.FireResult(OrderTrigger.Submit);
            return result.Tap(newState => State = newState);
        }
    }

    private enum OrderState
    {
        Draft,
        Submitted
    }

    private enum OrderTrigger
    {
        Submit
    }
}