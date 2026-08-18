namespace Trellis.Asp.Idempotency;

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IApplicationBuilder"/> extensions that mount the Trellis Idempotency-Key
/// middleware in the request pipeline.
/// </summary>
public static class IdempotencyApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts the <see cref="IdempotencyMiddleware"/> in the pipeline. Always throws at startup
    /// if <see cref="IdempotencyServiceCollectionExtensions.AddTrellisIdempotency(IServiceCollection, Action{IdempotencyOptions}?)"/>
    /// was not called. The <see cref="IIdempotencyStore"/> registration is also validated at
    /// startup when the container exposes <see cref="IServiceProviderIsService"/> (see the
    /// remarks for the conditional-validation rationale).
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// Store registration is verified via <see cref="IServiceProviderIsService"/> without
    /// resolving the service, so scoped <see cref="IIdempotencyStore"/> implementations (for
    /// example an EF-backed store that depends on a scoped <c>DbContext</c>) are validated at
    /// startup without being captured by the root provider. Containers that do not implement
    /// <see cref="IServiceProviderIsService"/> skip the registration check; missing
    /// registrations surface as a per-request resolution error when the middleware runs.
    /// </remarks>
    public static IApplicationBuilder UseTrellisIdempotency(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var marker = app.ApplicationServices.GetService<IdempotencyServiceCollectionExtensions.IdempotencyMarker>();
        if (marker is null)
        {
            throw new InvalidOperationException(
                "UseTrellisIdempotency() was called but AddTrellisIdempotency() was not. " +
                "Call services.AddTrellisIdempotency() at startup before mounting the middleware.");
        }

        var checker = app.ApplicationServices.GetService<IServiceProviderIsService>();
        if (checker is not null && !checker.IsService(typeof(IIdempotencyStore)))
        {
            throw new InvalidOperationException(
                "UseTrellisIdempotency() requires an IIdempotencyStore. " +
                "Call services.AddInMemoryIdempotencyStore() for an in-process store, " +
                "or register your own IIdempotencyStore implementation.");
        }

        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}