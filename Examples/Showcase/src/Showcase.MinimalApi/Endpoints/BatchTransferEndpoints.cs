namespace Trellis.Showcase.MinimalApi.Endpoints;

using System.Collections.Generic;
using global::Mediator;
using Trellis.Asp;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Demonstrates the v2 Mediator pipeline end-to-end: HTTP → command → unified validation
/// stage (IValidate + FluentValidation) → handler → HTTP response. See
/// <see cref="SubmitBatchTransfersCommand"/> for the full design narrative.
/// </summary>
public static class BatchTransferEndpoints
{
    /// <summary>Wire DTO carrying batch metadata and the line items submitted via JSON body.</summary>
    public sealed record BatchTransferRequest(BatchMetadata Metadata, IReadOnlyList<BatchTransferLine> Lines);

    public static IEndpointRouteBuilder MapBatchTransferEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/transfers/batch").WithTags("BatchTransfers");

        group.MapPost(
            "/{fromId:AccountId}",
            (AccountId fromId, BatchTransferRequest request, IMediator mediator, CancellationToken ct) =>
                mediator.Send(
                    new SubmitBatchTransfersCommand(fromId, request.Metadata, request.Lines),
                    ct)
                    .ToHttpResponseAsync())
            .WithScalarValueValidation();

        return routes;
    }
}
