namespace Trellis.Yarp.Tests;

using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy;
using global::Yarp.ReverseProxy.Transforms.Builder;

/// <summary>
/// Tests for <see cref="TrellisActorForwardingServiceCollectionExtensions.AddTrellisActorForwarding"/>.
/// Asserts the service-collection wiring (options binding, validator registration,
/// minter singleton, transform provider) and the ValidateOnStart contract.
/// </summary>
public sealed class AddTrellisActorForwardingTests
{
    [Fact]
    public void AddTrellisActorForwarding_RegistersOptionsValidatorAndMinter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var yarpBuilder = services.AddReverseProxy();

        yarpBuilder.AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Should().NotBeNull();
        sp.GetServices<IValidateOptions<TrellisActorForwardingOptions>>()
            .Should().ContainSingle(v => v is TrellisActorForwardingOptionsValidator);
        sp.GetRequiredService<TrellisActorJwtMinter>().Should().NotBeNull();
    }

    [Fact]
    public void AddTrellisActorForwarding_RegistersTimeProviderWhenAbsent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddTrellisActorForwarding_DoesNotReplaceUserRegisteredTimeProvider()
    {
        var custom = new FakeFixedTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(custom);
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(custom,
            "AddTrellisActorForwarding must use TryAddSingleton so consumers can pre-register a test TimeProvider (typical pattern in xUnit integration tests)");
    }

    [Fact]
    public void AddTrellisActorForwarding_RegistersTransformProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetServices<ITransformProvider>().Should().ContainSingle(p => p is TrellisActorForwardingTransformProvider);
    }

    [Fact]
    public void AddTrellisActorForwarding_ValidateOnStart_FailsOnInvalidOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(o =>
        {
            o.Issuer = ""; // invalid
            o.SigningCredentials = NewRsaSigningCredentials("active-1");
            o.PublicBaseUrl = new Uri("https://gateway.internal");
        });

        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>();

        // Touching .Value forces validation; the IValidateOptions<> instance runs and throws.
        var act = () => monitor.Value;
        act.Should().Throw<OptionsValidationException>()
           .WithMessage("*Issuer*");
    }

    [Fact]
    public void AddTrellisActorForwarding_NullBuilder_Throws()
    {
        IReverseProxyBuilder? builder = null;
        var act = () => builder!.AddTrellisActorForwarding(ConfigureValid);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisActorForwarding_NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddReverseProxy();
        var act = () => builder.AddTrellisActorForwarding(configure: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisActorForwarding_MinterIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        var minter1 = sp.GetRequiredService<TrellisActorJwtMinter>();
        var minter2 = sp.GetRequiredService<TrellisActorJwtMinter>();
        minter1.Should().BeSameAs(minter2,
            "the minter holds the singleton JsonWebTokenHandler and options instance — no per-request allocation");
    }

    // === Fixtures ===

    private static void ConfigureValid(TrellisActorForwardingOptions o)
    {
        o.Issuer = "https://gateway.internal";
        o.SigningCredentials = NewRsaSigningCredentials("active-1");
        o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute);
    }

    private static SigningCredentials NewRsaSigningCredentials(string kid) =>
        new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

    private sealed class FakeFixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
