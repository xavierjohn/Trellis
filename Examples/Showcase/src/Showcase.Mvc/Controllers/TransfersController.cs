namespace Trellis.Showcase.Mvc.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
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

    [HttpPost("{fromId:AccountId}")]
    public Task<ActionResult<AccountResponse>> Transfer(AccountId fromId, [FromBody] TransferRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(fromId)
            .Combine(_repository.GetById(request.ToAccountId))
            .BindAsync(pair => _workflow.TransferAsync(pair.Item1, pair.Item2, request.Amount, request.Description, cancellationToken))
            .MapAsync(pair => AccountResponse.From(pair.From))
            .ToHttpResponseAsync()
            .AsActionResultAsync<AccountResponse>();
}