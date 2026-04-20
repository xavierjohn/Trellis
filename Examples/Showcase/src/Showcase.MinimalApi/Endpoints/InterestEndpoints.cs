namespace Trellis.Showcase.MinimalApi.Endpoints;

using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

public static class InterestEndpoints
{
    public static IEndpointRouteBuilder MapInterestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/accounts/{id}/interest").WithTags("Interest");

        group.MapPost("/", (AccountId id, InterestRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.PayInterestAsync(account, request.AnnualRate, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        return routes;
    }
}
