namespace Trellis.Showcase.MinimalApi.Endpoints;

using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/transfers").WithTags("Transfers");

        group.MapPost("/{fromId}", (AccountId fromId, TransferRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(fromId)
                .Combine(repo.GetById(request.ToAccountId))
                .BindAsync(pair => workflow.TransferAsync(pair.Item1, pair.Item2, request.Amount, request.Description, ct))
                .MapAsync(pair => AccountResponse.From(pair.From))
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        return routes;
    }
}
