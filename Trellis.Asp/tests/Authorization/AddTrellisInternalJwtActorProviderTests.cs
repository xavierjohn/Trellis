namespace Trellis.Asp.Tests.Authorization;

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddTrellisInternalJwtActorProvider"/>.
/// Companion <c>TrellisServiceBuilder.UseTrellisInternalJwtActor</c> tests live in
/// <c>Trellis.ServiceDefaults.Tests.TrellisServiceBuilderTests</c>.
/// </summary>
public sealed class AddTrellisInternalJwtActorProviderTests
{
    [Fact]
    public void AddTrellisInternalJwtActorProvider_RegistersScopedActorProvider()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtActorProvider();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IActorProvider)
            && d.ImplementationType == typeof(TrellisInternalJwtActorProvider)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddTrellisInternalJwtActorProvider_ReplacesPriorActorProvider()
    {
        // Mirrors the AddXxxActorProvider semantics: actor-provider helpers do not stack;
        // calling a second one replaces the first.
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();

        services.AddTrellisInternalJwtActorProvider();

        services.Count(d => d.ServiceType == typeof(IActorProvider)).Should().Be(1);
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IActorProvider)
            && d.ImplementationType == typeof(TrellisInternalJwtActorProvider));
    }

    [Fact]
    public void AddTrellisInternalJwtActorProvider_RegistersOptionsValidator()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtActorProvider();

        services.Should().Contain(d =>
            d.ServiceType == typeof(IValidateOptions<TrellisInternalJwtActorOptions>)
            && d.ImplementationType == typeof(TrellisInternalJwtActorOptionsValidator));
    }

    [Fact]
    public void AddTrellisInternalJwtActorProvider_InvalidOptions_ThrowsAtIOptionsValueResolution()
    {
        // ValidateOnStart() makes invalid options surface as OptionsValidationException on
        // IOptions<T>.Value resolution. In production this fires at host start; in this
        // unit test we resolve directly to surface the same exception.
        var services = new ServiceCollection();
        services.AddTrellisInternalJwtActorProvider(o => o.PermissionsClaim = "iss"); // reserved → invalid

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TrellisInternalJwtActorOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*reserved JWT claim 'iss'*");
    }

    [Fact]
    public void AddTrellisInternalJwtActorProvider_NoConfigureDelegate_AppliesDefaults()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtActorProvider();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TrellisInternalJwtActorOptions>>().Value;
        options.AuthenticationScheme.Should().Be("Bearer");
        options.ActorIdClaim.Should().Be("sub");
        options.ExpectedContractVersion.Should().Be("1");
        options.StrictClaimShape.Should().BeTrue();
        options.VaryByHeaders.Should().BeEquivalentTo(["Authorization"]);
    }

    [Fact]
    public void AddTrellisInternalJwtActorProvider_RegistersHttpContextAccessor()
    {
        // Provider depends on IHttpContextAccessor — the extension must register it (the
        // pattern other AddXxxActorProvider helpers use).
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtActorProvider();

        services.Should().Contain(d => d.ServiceType == typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor));
    }
}
