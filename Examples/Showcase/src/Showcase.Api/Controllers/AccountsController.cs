namespace Trellis.Showcase.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;
using Trellis.Showcase.Api.Models;
using Trellis.Showcase.Api.Persistence;
using Trellis.Showcase.Api.Workflows;
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
        Money.TryCreate(request.InitialDeposit, request.Currency)
            .Combine(Money.TryCreate(request.DailyWithdrawalLimit, request.Currency))
            .Combine(Money.TryCreate(request.OverdraftLimit, request.Currency))
            .BindAsync(values =>
            {
                var (deposit, daily, overdraft) = values;
                return _workflow.OpenAccountAsync(request.CustomerId, request.AccountType, deposit, daily, overdraft, cancellationToken);
            })
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/deposit")]
    public Task<ActionResult<AccountResponse>> Deposit(AccountId id, [FromBody] DepositRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .BindAsync(pair => _workflow.DepositAsync(pair.Item1, pair.Item2, request.Description, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/withdraw")]
    public Task<ActionResult<AccountResponse>> Withdraw(AccountId id, [FromBody] WithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .BindAsync(pair => _workflow.WithdrawAsync(pair.Item1, pair.Item2, request.Description, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/secure-withdraw")]
    public Task<ActionResult<AccountResponse>> SecureWithdraw(AccountId id, [FromBody] SecureWithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .BindAsync(pair => _workflow.SecureWithdrawAsync(pair.Item1, pair.Item2, request.VerificationCode, cancellationToken))
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
