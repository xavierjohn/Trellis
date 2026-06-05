namespace Trellis.Yarp.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using global::Yarp.ReverseProxy.Transforms.Builder;

/// <summary>
/// Tests for <see cref="TrellisActorForwardingRegistrationValidator"/>. Asserts the
/// hosted-lifecycle check that fails fast at host startup when AddTrellisActorForwarding
/// was called but no IActorProvider is registered. The transform would otherwise fail
/// per-request with the generic "no service registered" message; this validator turns
/// it into a clear startup error pointing at the exact misconfiguration.
/// </summary>
public sealed class TrellisActorForwardingRegistrationValidatorTests
{
    [Fact]
    public async Task StartingAsync_ActorProviderRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(new StubActorProvider());
        var sp = services.BuildServiceProvider();
        var validator = new TrellisActorForwardingRegistrationValidator(sp);

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartingAsync_NoActorProviderRegistered_ThrowsWithGuidance()
    {
        var services = new ServiceCollection();   // intentionally no IActorProvider
        var sp = services.BuildServiceProvider();
        var validator = new TrellisActorForwardingRegistrationValidator(sp);

        var act = async () => await validator.StartingAsync(TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*AddTrellisActorForwarding*");
        ex.WithMessage("*IActorProvider*");
        ex.WithMessage("*AddClaimsActorProvider*");
    }

    [Fact]
    public async Task Host_StartsCleanly_WhenAddTrellisActorForwardingPairedWithActorProvider()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IActorProvider>(new StubActorProvider());
                    services.AddReverseProxy().AddTrellisActorForwarding(o =>
                    {
                        o.Issuer = "https://gateway.internal";
                        o.SigningCredentials = new global::Microsoft.IdentityModel.Tokens.SigningCredentials(
                            new global::Microsoft.IdentityModel.Tokens.RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "active-1" },
                            global::Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256);
                        o.PublicBaseUrl = new Uri("https://gateway.internal");
                    });
                });
                webHost.Configure(app => app.UseRouting());
            });

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);

        // Host started successfully; the validator's StartingAsync ran without throwing.
        host.Should().NotBeNull();
    }

    [Fact]
    public async Task Host_FailsStartup_WhenAddTrellisActorForwardingMissingActorProvider()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    // Intentionally no IActorProvider registration.
                    services.AddReverseProxy().AddTrellisActorForwarding(o =>
                    {
                        o.Issuer = "https://gateway.internal";
                        o.SigningCredentials = new global::Microsoft.IdentityModel.Tokens.SigningCredentials(
                            new global::Microsoft.IdentityModel.Tokens.RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "active-1" },
                            global::Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256);
                        o.PublicBaseUrl = new Uri("https://gateway.internal");
                    });
                });
                webHost.Configure(app => app.UseRouting());
            });

        var act = async () =>
        {
            using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        };

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*AddTrellisActorForwarding*");
        ex.WithMessage("*IActorProvider*");
    }

    private sealed class StubActorProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe<Actor>.None);
    }
}
