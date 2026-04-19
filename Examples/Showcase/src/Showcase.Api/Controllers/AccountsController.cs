namespace Trellis.Showcase.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;
using Trellis.Showcase.Api.Models;
using Trellis.Showcase.Api.Persistence;
using Trellis.Showcase.Api.Services;
using Trellis.Showcase.Api.Workflows;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly IAccountRepository _repository;
    private readonly BankingWorkflow _workflow;
    private readonly IClock _clock;

    public AccountsController(IAccountRepository repository, BankingWorkflow workflow, IClock clock)
    {
        _repository = repository;
        _workflow = workflow;
        _clock = clock;
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
    public ActionResult<AccountResponse> Open([FromBody] OpenAccountRequest request) =>
        Money.TryCreate(request.InitialDeposit, request.Currency)
            .Combine(Money.TryCreate(request.DailyWithdrawalLimit, request.Currency))
            .Combine(Money.TryCreate(request.OverdraftLimit, request.Currency))
            .Bind(values =>
            {
                var (deposit, daily, overdraft) = values;
                return BankAccount.TryCreate(request.CustomerId, request.AccountType, deposit, daily, overdraft, () => _clock.UtcNow);
            })
            .Tap(_repository.Add)
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id}/deposit")]
    public ActionResult<AccountResponse> Deposit(AccountId id, [FromBody] DepositRequest request) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .Bind(pair => pair.Item1.Deposit(pair.Item2, request.Description))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id}/withdraw")]
    public ActionResult<AccountResponse> Withdraw(AccountId id, [FromBody] WithdrawRequest request) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .Bind(pair => pair.Item1.Withdraw(pair.Item2, request.Description))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id}/secure-withdraw")]
    public Task<ActionResult<AccountResponse>> SecureWithdraw(AccountId id, [FromBody] SecureWithdrawRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .BindAsync(pair => _workflow.ProcessSecureWithdrawalAsync(pair.Item1, pair.Item2, request.VerificationCode, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);

    [HttpPost("{id}/freeze")]
    public ActionResult<AccountResponse> Freeze(AccountId id, [FromBody] FreezeRequest request) =>
        _repository.GetById(id)
            .Bind(account => account.Freeze(request.Reason))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id}/unfreeze")]
    public ActionResult<AccountResponse> Unfreeze(AccountId id) =>
        _repository.GetById(id)
            .Bind(account => account.Unfreeze())
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id}/close")]
    public ActionResult<AccountResponse> Close(AccountId id) =>
        _repository.GetById(id)
            .Bind(account => account.Close())
            .Map(AccountResponse.From)
            .ToActionResult(this);
}
