namespace CookbookSnippets.Tests;

using CookbookSnippets.Recipe16;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator;
using Trellis.Testing;
using Unit = Trellis.Unit;

public class Recipe16CommandHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_WithFakeUnitOfWork_RunsRealHandlerAndHookWithoutContainerOrDatabase()
    {
        var orders = new Orders();
        var audits = new Audits();
        var state = new CommandDispatchState();
        var unitOfWork = new TestUnitOfWork();
        var handler = new CreateAuditedOrderHandler(orders, state, TimeProvider.System);
        var hook = new RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>(
            state, audits, NullLogger<RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>>.Instance);
        var behavior = new TransactionalCommandBehavior<CreateAuditedOrderCommand, Result<Order>>(
            unitOfWork, [hook]);

        var result = await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), handler.Handle, Token);

        result.Should().BeSuccess();
        orders.GetAll().Should().ContainSingle().Which.Should().BeSameAs(result.Unwrap());
        audits.GetAll().Should().ContainSingle().Which.OrderId.Should().Be(result.Unwrap().Id);
        unitOfWork.Commits.Should().Be(1);
        FluentActions.Invoking(() => state.TryGet<PendingOrderAudits>(out _))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_FailedHandler_ClosesFrameWithoutRunningHookOrCommit(bool throws)
    {
        var orders = new Orders();
        var audits = new Audits();
        var state = new CommandDispatchState();
        var unitOfWork = new TestUnitOfWork();
        var handler = new CreateAuditedOrderHandler(orders, state, TimeProvider.System);
        var hook = new RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>(
            state, audits, NullLogger<RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>>.Instance);
        var behavior = new TransactionalCommandBehavior<CreateAuditedOrderCommand, Result<Order>>(
            unitOfWork, [hook]);
        async ValueTask<Result<Order>> Invoke() => await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), async (command, token) =>
            {
                (await handler.Handle(command, token)).Should().BeSuccess();
                if (throws)
                    throw new InvalidOperationException("Handler failed.");
                return Result.Fail<Order>(new Error.Conflict("order.rejected"));
            }, Token);

        if (throws)
        {
            var act = async () => await Invoke();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Handler failed.");
        }
        else
            (await Invoke()).Should().HaveErrorCode("order.rejected");

        audits.Count.Should().Be(0);
        unitOfWork.Commits.Should().Be(0);
        FluentActions.Invoking(() => state.TryGet<PendingOrderAudits>(out _))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_HookVeto_PreventsThisAndEveryLaterCommit()
    {
        var orders = new Orders();
        var audits = new Audits();
        var state = new CommandDispatchState();
        var unitOfWork = new TestUnitOfWork();
        var hook = new RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>(
            state, audits, NullLogger<RequiredAuditHook<CreateAuditedOrderCommand, Result<Order>>>.Instance);
        var behavior = new TransactionalCommandBehavior<CreateAuditedOrderCommand, Result<Order>>(
            unitOfWork, [hook]);

        var result = await behavior.Handle(new CreateAuditedOrderCommand(new Money(10m)),
            (command, _) => Order.TryCreate(command.Total).Tap(orders.Add).AsValueTask(), Token);

        result.Should().HaveErrorCode("audit.not-recorded");
        (await unitOfWork.CommitAsync(Token)).UnwrapError().Should().BeSameAs(result.UnwrapError());
        audits.Count.Should().Be(0);
        unitOfWork.Commits.Should().Be(0);
    }

    [Fact]
    public async Task TestUnitOfWork_NestedScopes_DefersAndPermanentlyPreservesFirstRejection()
    {
        var unitOfWork = new TestUnitOfWork();
        using (var owner = unitOfWork.BeginScope())
        {
            owner.IsOwner.Should().BeTrue();
            using (var inner = unitOfWork.BeginScope())
            {
                inner.IsOwner.Should().BeFalse();
                (await unitOfWork.CommitAsync(Token)).Should().BeSuccess();
                unitOfWork.Commits.Should().Be(0);
            }

            (await unitOfWork.CommitAsync(Token)).Should().BeSuccess();
            unitOfWork.Commits.Should().Be(1);
        }

        var first = new Error.Conflict("first.rejection");
        unitOfWork.MarkUncommittable(first);
        unitOfWork.MarkUncommittable(new Error.Conflict("second.rejection"));
        using (unitOfWork.BeginScope())
        using (unitOfWork.BeginScope())
            (await unitOfWork.CommitAsync(Token)).UnwrapError().Should().BeSameAs(first);
        (await unitOfWork.CommitAsync(Token)).UnwrapError().Should().BeSameAs(first);
        unitOfWork.Commits.Should().Be(1);
        FluentActions.Invoking(() => unitOfWork.MarkUncommittable(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("reason");
    }

    private sealed class Orders : FakeRepository<Order, OrderId>, IOrderRepository;
    private sealed class Audits : FakeRepository<AuditEntry, AuditEntryId>, IAuditEntryRepository;

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private int _depth;
        private Error? _rejection;
        public int Commits { get; private set; }

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_rejection is { } rejection)
                return Result.Fail(rejection).AsTask();
            if (_depth <= 1)
                Commits++;
            return Result.Ok().AsTask();
        }

        public void MarkUncommittable(Error reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            _rejection ??= reason;
        }

        public IUnitOfWorkScope BeginScope() => new Scope(this, ++_depth == 1);

        private sealed class Scope(TestUnitOfWork owner, bool isOwner) : IUnitOfWorkScope
        {
            private bool _disposed;
            public bool IsOwner { get; } = isOwner;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner._depth--;
            }
        }
    }
}
