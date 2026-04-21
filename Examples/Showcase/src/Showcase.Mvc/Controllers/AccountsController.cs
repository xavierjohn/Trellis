namespace Trellis.Showcase.Mvc.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly IAccountRepository _repository;
    private readonly BankingWorkflow _workflow;

    public AccountsController(IAccountRepository repository, BankingWorkflow workflow)
    {
        _repository = repository;
        _workflow = workflow;
    }

    [HttpGet(Name = "Showcase_GetAccounts")]
    public ActionResult<PagedResponse<AccountResponse>> List(
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        [FromServices] LinkGenerator links)
    {
        var requestedLimit = limit is int l && l > 0 ? l : 10;
        Cursor? cursorOpt = string.IsNullOrEmpty(cursor) ? null : new Cursor(cursor);

        return _repository.GetPage(requestedLimit, cursorOpt)
            .ToHttpResponse(
                nextUrlBuilder: (c, applied) =>
                    links.GetUriByName(HttpContext, "Showcase_GetAccounts",
                        values: new { limit = applied, cursor = c.Token })
                    ?? throw new InvalidOperationException("Route 'Showcase_GetAccounts' not registered."),
                body: AccountResponse.From)
            .AsActionResult<PagedResponse<AccountResponse>>();
    }

    [HttpGet("{id:AccountId}", Name = "Showcase_GetAccount")]
    public ActionResult<AccountResponse> Get(AccountId id) =>
        _repository.GetById(id)
            .ToHttpResponse(AccountResponse.From)
            .AsActionResult<AccountResponse>();

    [HttpPost]
    public Task<ActionResult<AccountResponse>> Open([FromBody] OpenAccountRequest request, CancellationToken cancellationToken) =>
        _workflow.OpenAccountAsync(request.CustomerId, request.AccountType, request.InitialDeposit, request.DailyWithdrawalLimit, request.OverdraftLimit, cancellationToken)
            .ToHttpResponseAsync(
                AccountResponse.From,
                opts => opts.CreatedAtAction(nameof(Get), a => new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = a.Id }))
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/deposit")]
    public Task<ActionResult<AccountResponse>> Deposit(AccountId id, [FromBody] DepositRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.DepositAsync(account, request.Amount, request.Description, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/withdraw")]
    public Task<ActionResult<AccountResponse>> Withdraw(AccountId id, [FromBody] WithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.WithdrawAsync(account, request.Amount, request.Description, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/secure-withdraw")]
    public Task<ActionResult<AccountResponse>> SecureWithdraw(AccountId id, [FromBody] SecureWithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.SecureWithdrawAsync(account, request.Amount, request.VerificationCode, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/freeze")]
    public Task<ActionResult<AccountResponse>> Freeze(AccountId id, [FromBody] FreezeRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.FreezeAsync(account, request.Reason, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/unfreeze")]
    public Task<ActionResult<AccountResponse>> Unfreeze(AccountId id, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.UnfreezeAsync(account, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();

    [HttpPost("{id:AccountId}/close")]
    public Task<ActionResult<AccountResponse>> Close(AccountId id, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.CloseAsync(account, cancellationToken))
            .ToHttpResponseAsync(AccountResponse.From)
            .AsActionResultAsync<AccountResponse>();
}
