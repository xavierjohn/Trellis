namespace Trellis.Showcase.Tests.Application;

using System.Threading;
using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.Domain.ValueObjects;

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

        result.TryGetError(out var error).Should().BeTrue("an empty batch is invalid input, not a runtime crash");
        var upc = error.Should().BeOfType<Error.UnprocessableContent>().Which;
        upc.Fields.Items.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("batch.empty");
    }
}