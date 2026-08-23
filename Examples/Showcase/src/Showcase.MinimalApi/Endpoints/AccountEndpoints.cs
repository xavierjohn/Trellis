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
    /// <param name="apiDescriptionUrl">
    /// Where this API's OpenAPI description is served, or <see langword="null"/> when it is not
    /// served at all. Passed in rather than hardcoded because the link is only advertised when the
    /// document actually exists: a <c>service-desc</c> relation pointing at a route that is not
    /// mapped is worse than no link, since a client follows it and gets a 404.
    /// </param>
    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder routes,
        string? apiDescriptionUrl = null)
    {
        var group = routes.MapGroup("/api/accounts").WithTags("Accounts");

        void AdvertiseDescription<T>(HttpResponseOptionsBuilder<T> opts)
        {
            if (apiDescriptionUrl is not null)
                opts.WithLink("service-desc", apiDescriptionUrl);
        }

        group.MapGet("/", (
            int? limit,
            string? cursor,
            IAccountRepository repo,
            LinkGenerator links,
            HttpContext http) =>
            {
                var requestedLimit = limit is int l && l > 0 ? l : 10;
                Cursor? cursorOpt = string.IsNullOrEmpty(cursor) ? null : new Cursor(cursor);
                return repo.GetPage(requestedLimit, cursorOpt)
                    .ToHttpResponse(
                        nextUrlBuilder: (c, applied) =>
                            links.GetUriByName(http, "Showcase_GetAccounts",
                                values: new Microsoft.AspNetCore.Routing.RouteValueDictionary
                                {
                                    ["limit"] = applied,
                                    ["cursor"] = c.Token,
                                })
                            ?? throw new InvalidOperationException("Route 'Showcase_GetAccounts' not registered."),
                        body: AccountResponse.From,
                        configure: AdvertiseDescription);
            })
            .WithName("Showcase_GetAccounts");

        group.MapGet("/{id:AccountId}", (AccountId id, IAccountRepository repo) =>
            repo.GetById(id)
                .ToHttpResponse(AccountResponse.From, AdvertiseDescription))
            .WithName("Showcase_GetAccount");

        group.MapPost("/", (OpenAccountRequest request, BankingWorkflow workflow, CancellationToken ct) =>
            workflow.OpenAccountAsync(request.CustomerId, request.AccountType, request.InitialDeposit, request.DailyWithdrawalLimit, request.OverdraftLimit, ct)
                .ToHttpResponseAsync(
                    AccountResponse.From,
                    opts => opts.CreatedAtRoute("Showcase_GetAccount", account => new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = account.Id.Value })))
            .WithScalarValueValidation();

        group.MapPost("/{id:AccountId}/deposit", (AccountId id, DepositRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.DepositAsync(account, request.Amount, request.Description, ct))
                .ToHttpResponseAsync(AccountResponse.From))
            .WithScalarValueValidation();

        group.MapPost("/{id:AccountId}/withdraw", (AccountId id, WithdrawRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.WithdrawAsync(account, request.Amount, request.Description, ct))
                .ToHttpResponseAsync(AccountResponse.From))
            .WithScalarValueValidation();

        group.MapPost("/{id:AccountId}/secure-withdraw", (AccountId id, SecureWithdrawRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.SecureWithdrawAsync(account, request.Amount, request.VerificationCode, ct))
                .ToHttpResponseAsync(AccountResponse.From))
            .WithScalarValueValidation();

        group.MapPost("/{id:AccountId}/freeze", (AccountId id, FreezeRequest request, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.FreezeAsync(account, request.Reason, ct))
                .ToHttpResponseAsync(AccountResponse.From))
            .WithScalarValueValidation();

        group.MapPost("/{id:AccountId}/unfreeze", (AccountId id, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.UnfreezeAsync(account, ct))
                .ToHttpResponseAsync(AccountResponse.From));

        group.MapPost("/{id:AccountId}/close", (AccountId id, IAccountRepository repo, BankingWorkflow workflow, CancellationToken ct) =>
            repo.GetById(id)
                .BindAsync(account => workflow.CloseAsync(account, ct))
                .ToHttpResponseAsync(AccountResponse.From));

        return routes;
    }
}