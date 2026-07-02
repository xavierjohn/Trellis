namespace Trellis.Asp.Authorization;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Fails fast at host start when <see cref="ServiceCollectionExtensions.AddEasyAuthActorProvider"/>
/// selected the Easy Auth actor provider but no Easy Auth authentication scheme
/// (<see cref="EasyAuthAuthenticationHandler"/>) is registered.
/// </summary>
/// <remarks>
/// <para>
/// The actor provider maps <c>HttpContext.User</c> claims to an <c>Actor</c> but does NOT
/// authenticate the request. Without the scheme (and <c>UseAuthentication()</c>),
/// <c>HttpContext.User</c> is never populated, the provider silently resolves no actor, and
/// every actor-requiring endpoint returns 401 — a fail-closed but confusingly silent
/// misconfiguration. This validator surfaces it loudly instead.
/// </para>
/// <para>
/// Runs in <see cref="IHostedLifecycleService.StartingAsync"/> (invoked for every hosted
/// service BEFORE any <see cref="IHostedService.StartAsync"/>), mirroring
/// <c>WorkerActorRegistrationValidator</c>. The scheme is matched by handler type, so a custom
/// scheme name passed to <c>AddEasyAuth(name, ...)</c> still satisfies the check.
/// </para>
/// </remarks>
internal sealed class EasyAuthSchemeRegistrationValidator(IServiceProvider rootServices) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var schemeProvider = rootServices.GetService<IAuthenticationSchemeProvider>();
        if (schemeProvider is not null)
        {
            var schemes = await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);
            if (schemes.Any(scheme => scheme.HandlerType == typeof(EasyAuthAuthenticationHandler)))
                return;
        }

        throw new InvalidOperationException(
            "AddEasyAuthActorProvider was called but no Easy Auth authentication scheme is registered. " +
            "The actor provider maps HttpContext.User claims to an Actor but does not authenticate the " +
            "request; without the scheme HttpContext.User is never populated and every actor-requiring " +
            "endpoint returns 401. Register the scheme with AddAuthentication(...).AddEasyAuth() and call " +
            "app.UseAuthentication().");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
