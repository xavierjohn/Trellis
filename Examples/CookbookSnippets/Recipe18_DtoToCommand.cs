// Cookbook Recipe 18 — DTO primitives to value-object command: no test-only Unwrap().
namespace CookbookSnippets.Recipe18;

using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;

public sealed record CreateCustomerRequest(string Email, string CustomerName);

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed record CustomerResponse(CustomerId Id, string Email, string CustomerName);

[StringLength(200, MinimumLength = 1)]
public sealed partial class CustomerName : RequiredString<CustomerName>;

public sealed record CreateCustomerCommand(EmailAddress Email, CustomerName CustomerName)
    : ICommand<Result<CustomerResponse>>
{
    public static Result<CreateCustomerCommand> TryCreate(CreateCustomerRequest request) =>
        Result.Combine(
                EmailAddress.TryCreate(request.Email, nameof(request.Email)),
                CustomerName.TryCreate(request.CustomerName, nameof(request.CustomerName)))
            .Map((email, customerName) => new CreateCustomerCommand(email, customerName));

    public static Result<CreateCustomerCommand> RequireValues(
        EmailAddress? email, CustomerName? customerName) =>
        Result.EnsureNotNull(email, "email", "Email is required.")
            .Combine(Result.EnsureNotNull(customerName, "customerName", "Customer name is required."))
            .Map((email, customerName) => new CreateCustomerCommand(email, customerName));

    public static Task<Result<CreateCustomerCommand>> RequireValuesAsync(
        Task<EmailAddress?> email, Task<CustomerName?> customerName) =>
        email.EnsureNotNullAsync("email", "Email is required.")
            .CombineAsync(customerName.EnsureNotNullAsync("customerName", "Customer name is required."))
            .MapAsync((email, customerName) => new CreateCustomerCommand(email, customerName));
}

[ApiController]
[Route("customers")]
public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken ct) =>
        CreateCustomerCommand.TryCreate(request)
            .BindAsync(command => sender.Send(command, ct))
            .ToHttpResponseAsync()
            .AsActionResultAsync<CustomerResponse>();
}

internal static class Recipe18Demonstrator
{
    public static Result<CreateCustomerCommand> CombineDtoFields(CreateCustomerRequest request) =>
        Result.Combine(
                EmailAddress.TryCreate(request.Email, nameof(request.Email)),
                CustomerName.TryCreate(request.CustomerName, nameof(request.CustomerName)))
            .Map((email, customerName) => new CreateCustomerCommand(email, customerName));

    public static Task<Result<CustomerResponse>> BindValidCommandAsync(CreateCustomerRequest request, ISender sender, CancellationToken ct) =>
        CreateCustomerCommand.TryCreate(request)
            .BindAsync(command => sender.Send(command, ct).AsTask());
}

#if FALSE
// WRONG — Unwrap() is test-only and turns validation failures into thrown exceptions.
// var command = new CreateCustomerCommand(
//     EmailAddress.TryCreate(request.Email).Unwrap(),
//     CustomerName.TryCreate(request.CustomerName).Unwrap());
#endif