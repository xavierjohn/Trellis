// Cookbook Recipe 14 — Optional fields in request DTOs: Maybe<TScalar> vs nullable transport.
namespace CookbookSnippets.Recipe14;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using global::Mediator;
using Trellis;
using Trellis.Asp;
using Trellis.Mediator;
using Trellis.Primitives;
using CookbookSnippets.Recipe13;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;

public sealed partial class PhoneNumber : RequiredString<PhoneNumber>;

// Pattern A — scalar Maybe<T> directly on the DTO.
// AddTrellisAsp() registers MaybeScalarValueJsonConverterFactory, MaybeModelBinder, and
// MaybeSuppressChildValidationMetadataProvider so this round-trips correctly.
public sealed record CreateCustomerRequestA(
    EmailAddress Email,
    Maybe<PhoneNumber> PhoneNumber);

public sealed record CreateCustomerCommandA(
    EmailAddress Email,
    Maybe<PhoneNumber> PhoneNumber) : ICommand<Result<CustomerSummary>>;

public sealed record CustomerSummary(EmailAddress Email);

[ApiController]
[Route("a/customers")]
public sealed class CustomersControllerA(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequestA request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateCustomerCommandA(request.Email, request.PhoneNumber), ct);
        return result.IsSuccess ? Ok() : BadRequest();
    }
}

// Pattern B — composite owned VO. Use nullable transport, adapt at the controller seam.
public sealed record CreateCustomerRequestB(
    EmailAddress Email,
    ShippingAddress? ShippingAddress);

public sealed record CreateCustomerCommandB(
    EmailAddress Email,
    Maybe<ShippingAddress> ShippingAddress) : ICommand<Result<CustomerSummary>>;

[ApiController]
[Route("b/customers")]
public sealed class CustomersControllerB(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequestB request, CancellationToken ct)
    {
        var shipping = request.ShippingAddress is null
            ? Maybe<ShippingAddress>.None
            : Maybe.From(request.ShippingAddress);

        var result = await sender.Send(new CreateCustomerCommandB(request.Email, shipping), ct);
        return result.IsSuccess ? Ok() : BadRequest();
    }
}

public static class WiringFix
{
    // FIX — call AddTrellisAsp() before AddControllers(); idempotent and configures
    // both MVC and Minimal API JSON pipelines for ScalarValue/Maybe support.
    public static IServiceCollection ConfigureMvc(IServiceCollection services)
    {
        services.AddTrellisAsp();
        services.AddControllers();
        return services;
    }
}
