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

    [HttpPost("{fromId}")]
    public Task<ActionResult<AccountResponse>> Transfer(AccountId fromId, [FromBody] TransferRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(fromId)
            .Combine(_repository.GetById(request.ToAccountId))
            .Combine(Money.TryCreate(request.Amount, request.Currency))
            .BindAsync(values =>
            {
                var (from, to, amount) = values;
                return _workflow.TransferAsync(from, to, amount, request.Description, cancellationToken);
            })
            .MapAsync(pair => AccountResponse.From(pair.From))
            .ToActionResultAsync(this);
}
