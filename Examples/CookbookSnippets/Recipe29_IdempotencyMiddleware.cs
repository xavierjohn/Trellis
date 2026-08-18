// Cookbook Recipe 29 — IETF Idempotency-Key middleware on POST / PATCH with UseTrellisIdempotency.
namespace CookbookSnippets.Recipe29;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Idempotency;
using Trellis.ServiceDefaults;

public sealed record CreatePaymentRequest(decimal Amount, string Currency);

public static class IdempotencySample
{
    public static void Configure(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTrellis(t => t
            .UseAsp()
            .UseProblemDetails()
            .UseIdempotency(opt =>
            {
                opt.Ttl = System.TimeSpan.FromHours(24);
                opt.MaxRequestBodyBytes = 256 * 1024;
            }));

        // dev / single-instance; swap for an EF-backed store in production
        builder.Services.AddInMemoryIdempotencyStore();

        var app = builder.Build();
        app.UseTrellisIdempotency();
        app.MapControllers();

        // Minimal API equivalent — attach the attribute as endpoint metadata.
        app.MapPost("/payments", CreatePaymentAsync).WithMetadata(new IdempotentAttribute());

        app.Run();
    }

    private static Task<IResult> CreatePaymentAsync(CreatePaymentRequest body, CancellationToken cancellationToken) =>
        Task.FromResult(Results.Created($"/payments/{System.Guid.NewGuid()}", body));
}

[ApiController]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    [HttpPost]
    [Idempotent]
    public Task<IActionResult> CreateAsync(
        [FromBody] CreatePaymentRequest body,
        CancellationToken cancellationToken) =>
        // handler returns 201 Created with the payment representation
        Task.FromResult<IActionResult>(Created($"/payments/{System.Guid.NewGuid()}", body));
}
