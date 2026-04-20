namespace Trellis.Showcase.MinimalApi.Endpoints;

using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Account endpoints. Mirrors <c>Trellis.Showcase.Mvc.Controllers.AccountsController</c>; the same
/// JSON DTOs, repository, and <see cref="BankingWorkflow"/> are reused — only the hosting style
/// differs.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/accounts").WithTags("Accounts");

        group.MapGet("/", (IAccountRepository repo) =>
            Results.Ok(repo.All().Select(AccountResponse.From).ToList()));

        group.MapGet("/{id}", (AccountId id, IAccountRepository repo) =>
            repo.GetById(id)
                .Map(AccountResponse.From)
                .ToHttpResult())
            .WithName("Showcase_GetAccount");

        group.MapPost("/", (OpenAccountRequest request, BankingWorkflow workflow, CancellationToken ct) =>
            workflow.OpenAccountAsync(request.CustomerId, request.AccountType, request.InitialDeposit, request.DailyWithdrawalLimit, request.OverdraftLimit, ct)
                .ToCreatedAtRouteHttpResultAsync(
                    routeName: "Showcase_GetAccount",
                    routeValues: account => new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = account.Id.Value },
                    map: AccountResponse.From))
            .WithScalarValueValidation();

        group.MapPost("/{id}/deposit", (AccountId id, DepositRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.DepositAsync(account, request.Amount, request.Description, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        group.MapPost("/{id}/withdraw", (AccountId id, WithdrawRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.WithdrawAsync(account, request.Amount, request.Description, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        group.MapPost("/{id}/secure-withdraw", (AccountId id, SecureWithdrawRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.SecureWithdrawAsync(account, request.Amount, request.VerificationCode, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        group.MapPost("/{id}/freeze", (AccountId id, FreezeRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.FreezeAsync(account, request.Reason, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync())
            .WithScalarValueValidation();

        group.MapPost("/{id}/unfreeze", (AccountId id, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.UnfreezeAsync(account, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync());

        group.MapPost("/{id}/close", (AccountId id, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.CloseAsync(account, ct))
                .MapAsync(AccountResponse.From)
                .ToHttpResultAsync());

        return routes;
    }
}
