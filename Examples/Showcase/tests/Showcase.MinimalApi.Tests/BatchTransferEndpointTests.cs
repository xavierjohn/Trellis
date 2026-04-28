namespace Trellis.Showcase.MinimalApi.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Primitives;
using Trellis.Showcase.Application;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.MinimalApi;
using Trellis.Showcase.MinimalApi.Endpoints;

/// <summary>
/// End-to-end tests for the v2 Mediator pipeline showcase: POST /api/transfers/batch.
///
/// <para>
/// Validates that the unified <c>ValidationBehavior</c> composes <c>IValidate</c> + the
/// FluentValidation adapter on the same message and aggregates every violation into one
/// 422 response, with FluentValidation property names normalized to RFC 6901 JSON Pointers
/// (nested <c>/Metadata/Reference</c>, indexer <c>/Lines/0/Memo</c>).
/// </para>
/// </summary>
public sealed class BatchTransferEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly Uri BatchEndpoint = new(
        $"/api/transfers/batch/{ShowcaseSeed.AliceCheckingId.Value}",
        UriKind.Relative);

    private readonly WebApplicationFactory<Program> _factory;

    public BatchTransferEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_batch_returns_200_with_receipt_summing_all_lines()
    {
        var client = _factory.CreateClient();
        var request = new BatchTransferEndpoints.BatchTransferRequest(
            new BatchMetadata("BATCH-2026-001", "Q1 payouts"),
            [
                new BatchTransferLine(ShowcaseSeed.AliceSavingsId, Money.Create(100m, "USD"), "Line 1"),
                new BatchTransferLine(ShowcaseSeed.BobCheckingId, Money.Create(250m, "USD"), "Line 2"),
            ]);

        var response = await client.PostAsJsonAsync(BatchEndpoint, request, JsonOptions, Ct);

        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<BatchTransferReceipt>(JsonOptions, Ct);
        receipt.Should().NotBeNull();
        receipt!.Reference.Should().Be("BATCH-2026-001");
        receipt.LineCount.Should().Be(2);
        receipt.TotalAmount.Amount.Should().Be(350m);
    }

    [Fact]
    public async Task IValidate_failure_alone_returns_422_with_pointer_for_self_transfer_line()
    {
        var client = _factory.CreateClient();
        var request = new BatchTransferEndpoints.BatchTransferRequest(
            new BatchMetadata("BATCH-2026-002", "Self loop"),
            [
                new BatchTransferLine(ShowcaseSeed.AliceCheckingId, Money.Create(50m, "USD"), "Loop"),
            ]);

        var response = await client.PostAsJsonAsync(BatchEndpoint, request, JsonOptions, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("Lines/0/ToAccountId");
        body.Should().Contain("A line may not target the source account.");
    }

    [Fact]
    public async Task FluentValidation_failure_alone_returns_422_with_normalized_nested_and_indexer_pointers()
    {
        var client = _factory.CreateClient();
        var request = new BatchTransferEndpoints.BatchTransferRequest(
            new BatchMetadata(string.Empty, "Missing reference"),
            [
                new BatchTransferLine(ShowcaseSeed.AliceSavingsId, Money.Create(10m, "USD"), string.Empty),
            ]);

        var response = await client.PostAsJsonAsync(BatchEndpoint, request, JsonOptions, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var body = await response.Content.ReadAsStringAsync(Ct);

        // Nested property: FluentValidation reports "Metadata.Reference" → normalized to RFC 6901 segments.
        body.Should().Contain("Metadata/Reference");
        // Indexer property: FluentValidation reports "Lines[0].Memo" → normalized to RFC 6901 segments.
        body.Should().Contain("Lines/0/Memo");
        // Confirm the dotted/bracketed raw forms did NOT leak through.
        body.Should().NotContain("Metadata.Reference");
        body.Should().NotContain("Lines[0].Memo");
    }

    [Fact]
    public async Task Both_validation_sources_aggregate_into_single_422_response()
    {
        // Single request that simultaneously violates IValidate (self-transfer) and
        // FluentValidation (bad reference + empty memo on line 0). Proves composition.
        var client = _factory.CreateClient();
        var request = new BatchTransferEndpoints.BatchTransferRequest(
            new BatchMetadata("not-a-batch-ref", "x"),
            [
                new BatchTransferLine(ShowcaseSeed.AliceCheckingId, Money.Create(5m, "USD"), string.Empty),
            ]);

        var response = await client.PostAsJsonAsync(BatchEndpoint, request, JsonOptions, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var body = await response.Content.ReadAsStringAsync(Ct);

        // From IValidate
        body.Should().Contain("Lines/0/ToAccountId");
        body.Should().Contain("A line may not target the source account.");
        // From FluentValidation, normalized to JSON Pointer segments
        body.Should().Contain("Metadata/Reference");
        body.Should().Contain("Lines/0/Memo");
    }

    [Fact]
    public async Task Empty_lines_returns_422_with_lines_pointer_from_IValidate()
    {
        var client = _factory.CreateClient();
        var request = new BatchTransferEndpoints.BatchTransferRequest(
            new BatchMetadata("BATCH-2026-003", "Empty"),
            []);

        var response = await client.PostAsJsonAsync(BatchEndpoint, request, JsonOptions, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("\"Lines\"");
        body.Should().Contain("At least one line is required.");
    }
}