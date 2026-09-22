namespace Trellis.Showcase.Tests.Application;

using System.Threading;
using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.Domain.ValueObjects;
using Trellis.Testing;

/// <summary>
/// Defense-in-depth tests for <see cref="SubmitBatchTransfersHandler"/>. The handler is
/// normally protected by the validation pipeline (the <c>IValidate</c> contract on the
/// command short-circuits on empty <c>Lines</c>), but a handler must still be safe when
/// invoked directly — e.g., from a test, a custom transport, or a misconfigured pipeline.
/// Failure modes here MUST be Result-based, never thrown exceptions.
/// </summary>
public class SubmitBatchTransfersHandlerTests
{
    private static readonly AccountId FromId = AccountId.NewUniqueV4();

    [Fact]
    public async Task Handle_with_empty_lines_returns_unprocessable_content_instead_of_throwing()
    {
        var command = new SubmitBatchTransfersCommand(
            FromId,
            new BatchMetadata("BATCH-2026-001", "empty batch"),
            []);
        var handler = new SubmitBatchTransfersHandler();

        var result = await handler.Handle(command, CancellationToken.None);

        var upc = result.Should().BeFailureOfType<Error.InvalidInput>(
            "an empty batch is invalid input, not a runtime crash").Which;
        upc.Fields.Items.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("batch.empty");
    }

    [Fact]
    public void Validate_Indexed_MultipleSelfTransfers_PreservesOriginalPositions()
    {
        var command = new SubmitBatchTransfersCommand(FromId, new("BATCH-001", "indexed validation"),
        [
            new(FromId, Money.Create(10m, "USD"), "first"),
            new(AccountId.NewUniqueV4(), Money.Create(20m, "USD"), "valid"),
            new(FromId, Money.Create(30m, "USD"), "third"),
        ]);

        var failure = command.Validate().Error.Should().BeOfType<Error.InvalidInput>().Which;

        failure.Fields.Items.Select(field => field.Field.Path)
            .Should().Equal(["/Lines/0/ToAccountId", "/Lines/2/ToAccountId"]);
        failure.Fields.Items.Should().OnlyContain(field => field.ReasonCode == "batch.self-transfer");
    }

    [Fact]
    public async Task Handle_MixedCurrencies_ReturnsOriginalValidationError()
    {
        var command = new SubmitBatchTransfersCommand(FromId, new("BATCH-001", "mixed currency"),
        [
            new(AccountId.NewUniqueV4(), Money.Create(10m, "USD"), "first"),
            new(AccountId.NewUniqueV4(), Money.Create(20m, "EUR"), "second"),
        ]);

        var result = await new SubmitBatchTransfersHandler().Handle(command, TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items
            .Should().ContainSingle().Which.ReasonCode.Should().Be("batch.mixed-currency");
    }

    [Fact]
    public async Task Handle_ValidLines_PreservesReceiptValues()
    {
        var command = new SubmitBatchTransfersCommand(FromId, new("BATCH-001", "valid"),
        [
            new(AccountId.NewUniqueV4(), Money.Create(10m, "USD"), "first"),
            new(AccountId.NewUniqueV4(), Money.Create(20m, "USD"), "second"),
        ]);

        var result = await new SubmitBatchTransfersHandler().Handle(command, TestContext.Current.CancellationToken);

        var receipt = result.Should().BeSuccess().Which;
        receipt.Reference.Should().Be("BATCH-001");
        receipt.LineCount.Should().Be(2);
        receipt.TotalAmount.Should().Be(Money.Create(30m, "USD"));
    }
}