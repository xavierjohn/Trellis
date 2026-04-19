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
[Route("api/transfers")]
public class TransfersController : ControllerBase
{
    private readonly IAccountRepository _repository;
    private readonly BankingWorkflow _workflow;

    public TransfersController(IAccountRepository repository, BankingWorkflow workflow)
    {
        _repository = repository;
        _workflow = workflow;
    }

    [HttpPost("{fromId:guid}")]
    public async Task<ActionResult<AccountResponse>> Transfer(Guid fromId, [FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var fromAccount = _repository.GetById(AccountId.TryCreate(fromId).Value);
        if (fromAccount.TryGetError(out var fromError))
            return fromError.ToActionResult<AccountResponse>(this);

        var toAccount = _repository.GetById(AccountId.TryCreate(request.ToAccountId).Value);
        if (toAccount.TryGetError(out var toError))
            return toError.ToActionResult<AccountResponse>(this);

        var amount = Money.TryCreate(request.Amount, request.Currency);
        if (amount.TryGetError(out var amountError))
            return amountError.ToActionResult<AccountResponse>(this);

        var transfer = await _workflow.ProcessTransferAsync(fromAccount.Value, toAccount.Value, amount.Value, request.Description, cancellationToken);
        return transfer
            .Map(pair => AccountResponse.From(pair.From))
            .ToActionResult(this);
    }
}
