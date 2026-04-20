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

    [HttpGet]
    public ActionResult<IReadOnlyList<AccountResponse>> List() =>
        Ok(_repository.All().Select(AccountResponse.From).ToList());

    [HttpGet("{id}")]
    public ActionResult<AccountResponse> Get(AccountId id) =>
        _repository.GetById(id)
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost]
    public Task<ActionResult<AccountResponse>> Open([FromBody] OpenAccountRequest request, CancellationToken cancellationToken) =>
        _workflow.OpenAccountAsync(request.CustomerId, request.AccountType, request.InitialDeposit, request.DailyWithdrawalLimit, request.OverdraftLimit, cancellationToken)
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/deposit")]
    public Task<ActionResult<AccountResponse>> Deposit(AccountId id, [FromBody] DepositRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.DepositAsync(account, request.Amount, request.Description, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/withdraw")]
    public Task<ActionResult<AccountResponse>> Withdraw(AccountId id, [FromBody] WithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.WithdrawAsync(account, request.Amount, request.Description, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/secure-withdraw")]
    public Task<ActionResult<AccountResponse>> SecureWithdraw(AccountId id, [FromBody] SecureWithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.SecureWithdrawAsync(account, request.Amount, request.VerificationCode, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/freeze")]
    public Task<ActionResult<AccountResponse>> Freeze(AccountId id, [FromBody] FreezeRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.FreezeAsync(account, request.Reason, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/unfreeze")]
    public Task<ActionResult<AccountResponse>> Unfreeze(AccountId id, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.UnfreezeAsync(account, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/close")]
    public Task<ActionResult<AccountResponse>> Close(AccountId id, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.CloseAsync(account, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);
}
