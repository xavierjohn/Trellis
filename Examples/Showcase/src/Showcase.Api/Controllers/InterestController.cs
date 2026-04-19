namespace Trellis.Showcase.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Showcase.Api.Models;
using Trellis.Showcase.Api.Persistence;
using Trellis.Showcase.Api.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

[ApiController]
[Route("api/accounts/{id:guid}/interest")]
public class InterestController : ControllerBase
{
    private readonly IAccountRepository _repository;
    private readonly BankingWorkflow _workflow;

    public InterestController(IAccountRepository repository, BankingWorkflow workflow)
    {
        _repository = repository;
        _workflow = workflow;
    }

    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Pay(Guid id, [FromBody] InterestRequest request, CancellationToken cancellationToken)
    {
        var account = _repository.GetById(AccountId.TryCreate(id).Value);
        if (account.TryGetError(out var error))
            return error.ToActionResult<AccountResponse>(this);

        var result = await _workflow.ProcessInterestPaymentAsync(account.Value, request.AnnualRate, cancellationToken);
        return result.Map(AccountResponse.From).ToActionResult(this);
    }
}
