namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trellis.Authorization;
using Trellis.Testing;

public class ActorIdentityProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddCachingActorProvider_DualProvider_RegistersOneSharedLazyScopedWrapper()
    {
        var services = new ServiceCollection();
        services.AddCachingActorProvider<DualProvider>();
        services.AddCachingActorProvider<DualProvider>();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        var actorProvider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var identityProvider = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();
        var inner = scope.ServiceProvider.GetRequiredService<DualProvider>();

        identityProvider.Should().BeSameAs(actorProvider);
        scope.ServiceProvider.GetServices<IActorIdentityProvider>().Should().ContainSingle();
        (await identityProvider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("canonical:user");
        (await identityProvider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("canonical:user");
        inner.IdentityCalls.Should().Be(1);
        inner.ActorCalls.Should().Be(0);
        (await actorProvider.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("canonical:user");
        inner.ActorCalls.Should().Be(1);

        using var anotherScope = root.CreateScope();
        anotherScope.ServiceProvider.GetRequiredService<IActorIdentityProvider>().Should().NotBeSameAs(identityProvider);
    }

    [Fact]
    public async Task AddCachingActorProvider_LegacyReplacement_RemovesIdentityWithoutChangingActorResolution()
    {
        var services = new ServiceCollection();
        services.AddCachingActorProvider<DualProvider>();
        services.AddCachingActorProvider<LegacyClaimsProvider>();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        scope.ServiceProvider.GetService<IActorIdentityProvider>().Should().BeNull();
        (await scope.ServiceProvider.GetRequiredService<IActorProvider>().GetCurrentActorAsync(Token))
            .Unwrap().Id.Value.Should().Be("custom:identity");
    }

    [Fact]
    public async Task AddClaimsActorProvider_DirectReplacement_IdentityDoesNotRetainPreviousProvider()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddScoped<IActorProvider, DualProvider>();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>().GetCurrentActorIdAsync(Token))
            .Unwrap().Value.Should().Be("canonical:user");
    }

    [Fact]
    public async Task AddClaimsActorProvider_UnsupportedDirectReplacement_ThrowsInsteadOfResolvingFullActor()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddScoped<IActorProvider, LegacyClaimsProvider>();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var act = async () =>
            await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>().GetCurrentActorIdAsync(Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LegacyClaimsProvider*IActorIdentityProvider*");
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task AddClaimsActorProvider_ClaimMapping_IdentityAgreesWithActor(string claimType)
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context(new Claim(claimType, " user "));
        using var scope = root.CreateScope();

        var id = (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap();
        var actor = (await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(Token)).Unwrap();

        id.Should().Be(actor.Id);
        id.Value.Should().Be("user");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddClaimsActorProvider_NoUsableIdentity_ReturnsNone(bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], authenticated ? "test" : null)),
        };
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Should().BeNone();
    }

    [Fact]
    public async Task AddEntraActorProvider_IdentityOnly_DoesNotInvokePermissionOrAttributeMappings()
    {
        var services = new ServiceCollection();
        services.AddEntraActorProvider(options =>
        {
            options.MapPermissions = _ => throw new InvalidOperationException("permissions");
            options.MapForbiddenPermissions = _ => throw new InvalidOperationException("denies");
            options.MapAttributes = (_, _) => throw new InvalidOperationException("attributes");
        });
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context(new Claim("oid", "entra:user"));
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("entra:user");
        var full = () => scope.ServiceProvider.GetRequiredService<IActorProvider>().GetCurrentActorAsync(Token);
        await full.Should().ThrowAsync<InvalidOperationException>().WithMessage("*MapPermissions*");
    }

    [Theory]
    [InlineData("""{"identity":{"id":" nested:user "},"roles":["read"]}""", "nested:user")]
    [InlineData("""{"roles":["read"]}""", "flat:user")]
    [InlineData("not-json", "flat:user")]
    public async Task AddNestedJsonPathClaimsActorProvider_IdentityMapping_AgreesWithFullActor(
        string container, string expected)
    {
        var services = new ServiceCollection();
        services.AddNestedJsonPathClaimsActorProvider(options =>
        {
            options.ContainerClaim = "metadata";
            options.ActorIdPath = "identity.id";
            options.PermissionsPath = "roles";
        });
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context(
            new Claim("metadata", container), new Claim("sub", "flat:user"));
        using var scope = root.CreateScope();

        var id = (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap();
        var actor = (await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(Token)).Unwrap();

        id.Value.Should().Be(expected);
        id.Should().Be(actor.Id);
    }

    [Fact]
    public async Task AddTrellisWorkerActor_OutsideHttp_IdentityDoesNotConstructHttpProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider>(_ => throw new InvalidOperationException("HTTP provider must remain lazy"));
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("system");
    }

    [Fact]
    public async Task AddTrellisWorkerActor_InsideHttp_IdentityUsesSelectedCachedProvider()
    {
        var services = new ServiceCollection();
        services.AddCachingActorProvider<DualProvider>();
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context();
        using var scope = root.CreateScope();
        var actor = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();

        identity.Should().BeSameAs(actor);
        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("canonical:user");
        scope.ServiceProvider.GetRequiredService<DualProvider>().ActorCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddTrellisWorkerActor_KnownUnsupportedProvider_DoesNotRegisterIdentity(bool useInstance)
    {
        var services = new ServiceCollection();
        if (useInstance)
            services.AddSingleton<IActorProvider>(new LegacyClaimsProvider());
        else
            services.AddScoped<IActorProvider, LegacyClaimsProvider>();
        services.AddScoped<IActorIdentityProvider, DualProvider>();
        var keyed = new DualProvider();
        services.AddKeyedSingleton<IActorIdentityProvider>("other", keyed);
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();

        scope.ServiceProvider.GetService<IActorIdentityProvider>().Should().BeNull();
        scope.ServiceProvider.GetRequiredKeyedService<IActorIdentityProvider>("other").Should().BeSameAs(keyed);
        var actor = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        (await actor.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("system");
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context();
        (await actor.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("custom:identity");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddTrellisWorkerActor_KnownSupportedProvider_RegistersSharedIdentity(bool useInstance)
    {
        var services = new ServiceCollection();
        if (useInstance)
            services.AddSingleton<IActorProvider>(new DualProvider());
        else
            services.AddScoped<IActorProvider, DualProvider>();
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();

        identity.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<IActorProvider>());
        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("system");
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context();
        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("canonical:user");
    }

    [Fact]
    public async Task AddTrellisWorkerActor_BuiltInProvider_RegistersIdentityAdapter()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context(new Claim("sub", "user"));
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("user");
    }

    [Fact]
    public async Task AddTrellisWorkerActor_UnknownFactory_KeepsLazyIdentityAndRejectsUnsupportedHttpProvider()
    {
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider>(_ =>
        {
            factoryCalls++;
            return new LegacyClaimsProvider();
        });
        services.AddTrellisWorkerActor(Actor.Create("system", new HashSet<string>()));
        factoryCalls.Should().Be(0);
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();

        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("system");
        factoryCalls.Should().Be(0);
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context();
        var act = async () => await identity.GetCurrentActorIdAsync(Token);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LegacyClaimsProvider*IActorIdentityProvider*");
        factoryCalls.Should().Be(1);
        (await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("custom:identity");
    }

    private static DefaultHttpContext Context(params Claim[] claims) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
    };

    [Theory]
    [InlineData(null, true, "development")]
    [InlineData("""{"iD":" dev:user ","Permissions":["read"]}""", true, "dev:user")]
    [InlineData("malformed", false, "development")]
    [InlineData("""{"Permissions":[]}""", false, "development")]
    [InlineData("""{"Id":"dev","Permissions":[123]}""", false, "development")]
    [InlineData("""{"Id":"dev","ForbiddenPermissions":[true]}""", false, "development")]
    [InlineData("""{"Id":"dev","Attributes":{"tid":123}}""", false, "development")]
    [InlineData("""{"Id":"dev","pErMiSsIoNs":[{}]}""", false, "development")]
    [InlineData("""{"Id":"dev","Permissions":[null,"read"],"Attributes":{"tid":null}}""", false, "dev")]
    [InlineData("""{"Id":"dev","Permissions":123,"Attributes":[]}""", false, "dev")]
    public async Task AddDevelopmentActorProvider_HeaderOrFallback_IdentityAgreesWithFullActor(
        string? header, bool strict, string expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
        services.AddDevelopmentActorProvider(options => options.ThrowOnMalformedHeader = strict);
        using var root = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers[DevelopmentActorProvider.HeaderName] = header;
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        using var scope = root.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();
        var actor = scope.ServiceProvider.GetRequiredService<IActorProvider>();

        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be(expected);
        await actor.ShouldHaveConsistentActorIdentityAsync(identity, Token);
    }

    [Fact]
    public async Task AddDevelopmentActorProvider_IdentityOnly_DoesNotParsePermissionValues()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
        services.AddDevelopmentActorProvider();
        using var root = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers[DevelopmentActorProvider.HeaderName] = """{"Id":"dev","Permissions":[{"invalid":true}]}""";
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        using var scope = root.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("dev");
        var full = () => scope.ServiceProvider.GetRequiredService<IActorProvider>().GetCurrentActorAsync(Token);
        await full.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddDevelopmentActorProvider_StrictMalformedOrProduction_IdentityFailsExplicitly(bool production)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment
        {
            EnvironmentName = production ? Environments.Production : Environments.Development,
        });
        services.AddDevelopmentActorProvider();
        using var root = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers[DevelopmentActorProvider.HeaderName] = "malformed";
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        using var scope = root.CreateScope();
        var act = async () => await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(production ? "*not allowed outside Development*" : "*Malformed*");
    }

    [Fact]
    public async Task AddEasyAuthActorProvider_AuthenticatedPrincipal_IdentityUsesConfiguredClaimMapping()
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddEasyAuth();
        services.AddEasyAuthActorProvider(options => options.ActorIdClaim = "name");
        using var root = services.BuildServiceProvider();
        root.GetRequiredService<IHttpContextAccessor>().HttpContext = Context(new Claim(ClaimTypes.Name, " easy:user "));
        using var scope = root.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();
        var actor = scope.ServiceProvider.GetRequiredService<IActorProvider>();

        (await identity.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("easy:user");
        await actor.ShouldHaveConsistentActorIdentityAsync(identity, Token);
    }

    [Fact]
    public void AddCachingActorProvider_Replacement_PreservesKeyedIdentityServices()
    {
        var keyed = new DualProvider();
        var services = new ServiceCollection();
        services.AddKeyedScoped<IActorIdentityProvider>("other", (_, _) => keyed);
        services.AddCachingActorProvider<DualProvider>();
        services.AddCachingActorProvider<LegacyClaimsProvider>();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IActorIdentityProvider>("other").Should().BeSameAs(keyed);
        scope.ServiceProvider.GetService<IActorIdentityProvider>().Should().BeNull();
    }

    [Fact]
    public async Task AddClaimsActorProvider_CancelledIdentity_PropagatesCancellation()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = async () => await scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>()
            .GetCurrentActorIdAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IdentityTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public sealed class DualProvider : IActorProvider, IActorIdentityProvider
    {
        public int IdentityCalls { get; private set; }
        public int ActorCalls { get; private set; }

        public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
        {
            IdentityCalls++;
            return ValueTask.FromResult(Maybe.From(ActorId.Create("canonical:user")));
        }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            ActorCalls++;
            return Task.FromResult(Maybe.From(Actor.Create("canonical:user", new HashSet<string>())));
        }
    }

    public sealed class LegacyClaimsProvider() : ClaimsActorProvider(
        new HttpContextAccessor(), Microsoft.Extensions.Options.Options.Create(new ClaimsActorOptions()))
    {
        public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(Actor.Create("custom:identity", new HashSet<string>())));
    }
}