namespace CookbookSnippets.Tests;

using CookbookSnippets.Recipe16;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Testing;

public class Recipe16AuditHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_DirectHandler_RecordsWithoutPipelineContainerOrUnitOfWork()
    {
        var orders = new Orders();
        var recorder = new OrderAuditRecorder();
        var handler = new CreateAuditedOrderHandler(orders, recorder, TimeProvider.System);

        var result = await handler.Handle(new CreateAuditedOrderCommand(new Money(10m)), Token);

        orders.GetAll().Should().ContainSingle().Which.Should().BeSameAs(result.Unwrap());
        recorder.Recorded.Should().ContainSingle().Which.OrderId.Should().Be(result.Unwrap().Id);
    }

    [Fact]
    public async Task Handle_RealHandlerAndBehavior_StagesAuditsAndClearsRecorder()
    {
        var orders = new Orders();
        var audits = new Audits();
        var recorder = new OrderAuditRecorder();
        var handler = new CreateAuditedOrderHandler(orders, recorder, TimeProvider.System);
        var behavior = CreateBehavior(recorder, audits);

        var result = await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), handler.Handle, Token);

        audits.GetAll().Should().ContainSingle().Which.OrderId.Should().Be(result.Unwrap().Id);
        recorder.Recorded.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("cancellation")]
    public async Task Handle_UnsuccessfulHandler_CleansRecorderWithoutStagingAudits(string outcome)
    {
        var orders = new Orders();
        var audits = new Audits();
        var recorder = new OrderAuditRecorder();
        var handler = new CreateAuditedOrderHandler(orders, recorder, TimeProvider.System);
        var behavior = CreateBehavior(recorder, audits);
        Exception original = outcome == "cancellation"
            ? new OperationCanceledException(Token)
            : new InvalidOperationException("Handler failed.");

        async Task<Result<Order>> Invoke() => await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), async (command, token) =>
            {
                (await handler.Handle(command, token)).Should().BeSuccess();
                await Task.Yield();
                if (outcome != "failure")
                    throw original;
                return Result.Fail<Order>(new Error.Conflict("order.rejected"));
            }, Token);

        if (outcome == "failure")
            (await Invoke()).Should().HaveErrorCode("order.rejected");
        else
        {
            var act = () => Invoke();
            (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(original);
        }

        audits.Count.Should().Be(0);
        recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CancellationAfterHandler_CleansRecorderBeforeStaging()
    {
        var recorder = new OrderAuditRecorder();
        var audits = new Audits();
        var handler = new CreateAuditedOrderHandler(new Orders(), recorder, TimeProvider.System);
        var behavior = CreateBehavior(recorder, audits);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var act = async () => await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), async (command, token) =>
            {
                var response = await handler.Handle(command, token);
                await cancellation.CancelAsync();
                return response;
            }, cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken
            .Should().Be(cancellation.Token);
        audits.Count.Should().Be(0);
        recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StagingException_PreservesExceptionAndCleansRecorder()
    {
        var recorder = new OrderAuditRecorder();
        var handler = new CreateAuditedOrderHandler(new Orders(), recorder, TimeProvider.System);
        var original = new InvalidOperationException("Staging failed.");
        var behavior = CreateBehavior(recorder, new ThrowingAudits(original));
        var act = async () => await behavior.Handle(
            new CreateAuditedOrderCommand(new Money(10m)), handler.Handle, Token);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
        recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyParentRecorded_RejectsMissingChildAuditAndPreservesParent()
    {
        var recorder = new OrderAuditRecorder();
        var parent = AuditEntry.ForOrderCreated(OrderId.NewUniqueV4(), TimeProvider.System);
        recorder.Record(parent);
        var audits = new Audits();
        var behavior = CreateBehavior(recorder, audits);

        var result = await behavior.Handle(new CreateAuditedOrderCommand(new Money(10m)),
            (command, _) => Order.TryCreate(command.Total).AsValueTask(), Token);

        result.Should().HaveErrorCode("audit.not-recorded");
        (result is IPersistOnFailure { PersistOnFailure: true }).Should().BeFalse();
        audits.Count.Should().Be(0);
        recorder.Recorded.Should().Equal([parent]);
    }

    private static RequiredAuditBehavior<CreateAuditedOrderCommand, Result<Order>> CreateBehavior(
        IOrderAuditRecorder recorder, IAuditEntryRepository audits) =>
        new(recorder, audits, NullLogger<RequiredAuditBehavior<CreateAuditedOrderCommand, Result<Order>>>.Instance);

    private sealed class Orders : FakeRepository<Order, OrderId>, IOrderRepository;
    private sealed class Audits : FakeRepository<AuditEntry, AuditEntryId>, IAuditEntryRepository;
    private sealed class ThrowingAudits(Exception exception) : IAuditEntryRepository
    {
        public void Add(AuditEntry entry) => throw exception;
    }
}