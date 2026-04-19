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

    [HttpGet("{id:guid}")]
    public ActionResult<AccountResponse> Get(Guid id) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost]
    public ActionResult<AccountResponse> Open([FromBody] OpenAccountRequest request)
    {
        if (!Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out var type))
        {
            return new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(nameof(request.AccountType)), "validation.enum")
                {
                    Detail = $"Unknown account type '{request.AccountType}'.",
                })).ToActionResult<AccountResponse>(this);
        }

        var moneyChecks = Money.TryCreate(request.InitialDeposit, request.Currency)
            .Combine(Money.TryCreate(request.DailyWithdrawalLimit, request.Currency))
            .Combine(Money.TryCreate(request.OverdraftLimit, request.Currency));

        return moneyChecks
            .Bind(values =>
            {
                var (deposit, daily, overdraft) = values;
                return BankAccount.TryCreate(
                    CustomerId.TryCreate(request.CustomerId).Value,
                    type,
                    deposit,
                    daily,
                    overdraft,
                    () => _clock.UtcNow);
            })
            .Tap(_repository.Add)
            .Map(AccountResponse.From)
            .ToActionResult(this);
    }

    [HttpPost("{id:guid}/deposit")]
    public ActionResult<AccountResponse> Deposit(Guid id, [FromBody] DepositRequest request) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Bind(account => Money.TryCreate(request.Amount, request.Currency).Map(money => (account, money)))
            .Bind(pair => pair.account.Deposit(pair.money, request.Description))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id:guid}/withdraw")]
    public ActionResult<AccountResponse> Withdraw(Guid id, [FromBody] WithdrawRequest request) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Bind(account => Money.TryCreate(request.Amount, request.Currency).Map(money => (account, money)))
            .Bind(pair => pair.account.Withdraw(pair.money, request.Description))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id:guid}/secure-withdraw")]
    public async Task<ActionResult<AccountResponse>> SecureWithdraw(Guid id, [FromBody] SecureWithdrawRequest request, CancellationToken cancellationToken)
    {
        var account = _repository.GetById(AccountId.TryCreate(id).Value);
        if (account.TryGetError(out var error))
            return error.ToActionResult<AccountResponse>(this);

        var amount = Money.TryCreate(request.Amount, request.Currency);
        if (amount.TryGetError(out var amountError))
            return amountError.ToActionResult<AccountResponse>(this);

        var withdrawal = await _workflow.ProcessSecureWithdrawalAsync(account.Value, amount.Value, request.VerificationCode, cancellationToken);
        return withdrawal.Map(AccountResponse.From).ToActionResult(this);
    }

    [HttpPost("{id:guid}/freeze")]
    public ActionResult<AccountResponse> Freeze(Guid id, [FromBody] FreezeRequest request) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Bind(account => account.Freeze(request.Reason))
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id:guid}/unfreeze")]
    public ActionResult<AccountResponse> Unfreeze(Guid id) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Bind(account => account.Unfreeze())
            .Map(AccountResponse.From)
            .ToActionResult(this);

    [HttpPost("{id:guid}/close")]
    public ActionResult<AccountResponse> Close(Guid id) =>
        _repository.GetById(AccountId.TryCreate(id).Value)
            .Bind(account => account.Close())
            .Map(AccountResponse.From)
            .ToActionResult(this);
}
