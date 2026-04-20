namespace Trellis.Showcase.Mvc.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis.Asp;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.ValueObjects;

[ApiController]
[Route("api/accounts/{id:AccountId}/interest")]
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
    public Task<ActionResult<AccountResponse>> Pay(AccountId id, [FromBody] InterestRequest request, CancellationToken cancellationToken) =>
        _repository.GetById(id)
            .BindAsync(account => _workflow.PayInterestAsync(account, request.AnnualRate, cancellationToken))
            .MapAsync(AccountResponse.From)
            .ToActionResultAsync(this);
}
