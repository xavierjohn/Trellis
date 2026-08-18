namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

/// <summary>
/// Tests for <c>VaryForActor()</c> on <see cref="HttpResponseOptionsBuilder{TDomain}"/>:
/// the new helper that emits the request header(s) contributing to actor identity into
/// the response <c>Vary</c> header, so intermediate HTTP caches don't serve actor A's
/// response to actor B. Pins the fail-closed behavior — the builder throws rather than
/// silently emit an incorrect or incomplete <c>Vary</c> header when the registered
/// provider does not implement <see cref="IProvideActorVaryHeaders"/>.
/// </summary>
public sealed class VaryForActorTests
{
    private sealed record Thing(int Id, string Name);

    private static DefaultHttpContext NewContext(IActorProvider? provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        if (provider is not null)
            services.AddSingleton(provider);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private sealed class TestActorProvider(IReadOnlyCollection<string>? vary)
        : IActorProvider, IProvideActorVaryHeaders
    {
        private static readonly System.Collections.Frozen.FrozenSet<string> EmptyPermissions =
            System.Collections.Frozen.FrozenSet<string>.Empty;

        public IReadOnlyCollection<string> VaryByHeaders { get; } = vary ?? [];
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe.From(Actor.Create("test-actor", EmptyPermissions)));
    }

    private sealed class ProviderWithoutVaryCapability : IActorProvider
    {
        private static readonly System.Collections.Frozen.FrozenSet<string> EmptyPermissions =
            System.Collections.Frozen.FrozenSet<string>.Empty;

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe.From(Actor.Create("test-actor", EmptyPermissions)));
    }

    [Fact]
    public async Task VaryForActor_emits_Authorization_for_bearer_style_provider()
    {
        // Bearer-style provider declares Authorization as the request header that contributes
        // to actor identity. VaryForActor() emits exactly that header.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization",
            "VaryForActor() must emit each header named by the provider's VaryByHeaders");
    }

    [Fact]
    public async Task VaryForActor_emits_test_header_for_DevelopmentActorProvider_style_provider()
    {
        // A non-bearer provider (e.g. DevelopmentActorProvider) varies by a different header.
        // VaryForActor() emits the provider's declared header verbatim — not a hard-coded
        // "Authorization" default. This is the defense against cache-poisoning for non-bearer
        // auth schemes.
        var ctx = NewContext(new TestActorProvider(["X-Test-Actor"]));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("X-Test-Actor");
    }

    [Fact]
    public async Task VaryForActor_emits_multiple_headers_when_provider_declares_them()
    {
        // A custom provider may legitimately vary by multiple headers (e.g. Authorization +
        // X-Forwarded-Tenant for tenant-scoped caching). VaryForActor() emits every header
        // the provider declares.
        var ctx = NewContext(new TestActorProvider(["Authorization", "X-Forwarded-Tenant"]));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization").And.Contain("X-Forwarded-Tenant");
    }

    [Fact]
    public async Task VaryForActor_deduplicates_against_existing_Vary_entries()
    {
        // Composes with explicit .Vary(...) calls: when a header is already in Vary
        // (whether from earlier middleware or from .Vary("Authorization") on the same chain),
        // VaryForActor() does not append a duplicate. Same idempotence semantics as the
        // existing AppendVaryUnique helper.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        ctx.Response.Headers["Vary"] = "Authorization";
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        // Combine all values and split by comma to count occurrences, since ASP.NET Core may
        // store Vary entries as one combined string or multiple header values.
        var combined = string.Join(",", ctx.Response.Headers.Vary.ToArray()!);
        var occurrences = combined
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Count(p => string.Equals(p, "Authorization", StringComparison.OrdinalIgnoreCase));
        occurrences.Should().Be(1, "VaryForActor() must dedupe against existing Vary entries (case-insensitive)");
    }

    [Fact]
    public async Task VaryForActor_throws_when_no_IActorProvider_registered()
    {
        // Fail-closed: calling VaryForActor() on a request that has no IActorProvider
        // registered is a configuration error. Silently emitting nothing would still let
        // an intermediate cache pollute responses across actors; throwing surfaces the
        // bug at the first request rather than via a security incident.
        var ctx = NewContext(provider: null);
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IActorProvider*");
    }

    [Fact]
    public async Task VaryForActor_throws_when_provider_does_not_implement_IProvideActorVaryHeaders()
    {
        // Fail-closed: a custom IActorProvider implementation that has not opted into
        // IProvideActorVaryHeaders cannot tell us which headers to emit. Silently falling
        // back to "Authorization" would be unsafe (cookie/mTLS/forwarded-header auth would
        // still cache-poison). The exception names the offending provider type and points
        // to the remedy.
        var ctx = NewContext(new ProviderWithoutVaryCapability());
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IProvideActorVaryHeaders*");
    }

    [Fact]
    public async Task VaryForActor_throws_when_provider_declares_empty_VaryByHeaders()
    {
        // A provider that explicitly declares an empty VaryByHeaders set is saying "I derive
        // actor identity from request data that can't be cleanly named by an HTTP header"
        // (mTLS, IP-based, etc.). The provider has opted into IProvideActorVaryHeaders but
        // is signaling that VaryForActor() is the wrong tool for this endpoint — endpoints
        // backed by that provider should use Cache-Control: private, no-store instead.
        var ctx = NewContext(new TestActorProvider(vary: []));
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task VaryForActor_composes_with_explicit_Vary_call()
    {
        // Mixing .VaryForActor() and .Vary("X") is valid: the response carries every header
        // either path requested. Common shape for endpoints that vary by both actor and
        // content negotiation (Accept, Accept-Language).
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.Vary("Accept-Language").VaryForActor())
            .ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization").And.Contain("Accept-Language");
    }

    [Fact]
    public async Task VaryForActor_applies_to_WriteOutcome_path()
    {
        // The success path for Result<WriteOutcome<T>> goes through TrellisWriteOutcomeResult
        // rather than TrellisHttpResult. VaryForActor() must plug into both code paths,
        // otherwise endpoints returning WriteOutcome (Created/Updated/Accepted) would
        // silently miss the Vary header and cache-poison across actors.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var outcome = new WriteOutcome<Thing>.Updated(new Thing(1, "x"));
        var r = Result.Ok<WriteOutcome<Thing>>(outcome);

        await r.ToHttpResponse<Thing>(o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization", "VaryForActor() must apply to the WriteOutcome success path too");
    }

    [Fact]
    public void ClaimsActorProvider_default_VaryByHeaders_is_Authorization()
    {
        // Pins the JWT-bearer default that the bundled ClaimsActorProvider /
        // EntraActorProvider rely on. If this changes, callers using AddClaimsActorProvider /
        // AddEntraActorProvider + VaryForActor() get a different shape on the wire — that
        // is a breaking change that should require a code review and CHANGELOG note.
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var options = Microsoft.Extensions.Options.Options.Create(new ClaimsActorOptions());
        var provider = new ClaimsActorProvider(httpContextAccessor, options);

        provider.Should().BeAssignableTo<IProvideActorVaryHeaders>();
        ((IProvideActorVaryHeaders)provider).VaryByHeaders.Should().BeEquivalentTo(["Authorization"]);
    }

    [Fact]
    public void CachingActorProvider_delegates_VaryByHeaders_to_inner_provider()
    {
        // CachingActorProvider wraps another provider. Its VaryByHeaders must surface the
        // inner provider's declaration, otherwise wrapping a bearer-style provider in
        // AddCachingActorProvider<T>() would silently strip the Vary capability.
        var inner = new TestActorProvider(["X-Test-Actor"]);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var cache = new CachingActorProvider(inner, httpContextAccessor);

        ((IProvideActorVaryHeaders)cache).VaryByHeaders.Should().BeEquivalentTo(["X-Test-Actor"]);
    }

    [Fact]
    public async Task VaryForActor_applies_on_failure_path_too()
    {
        // Cacheable failures (e.g. 404 Not Found that depends on actor visibility, or
        // validation 422 that depends on actor-scoped business rules) must partition by
        // actor in intermediate caches just like successful responses. Apply the actor-vary
        // header before WriteAsync so the failure response carries it.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var r = Result.Fail<Thing>(new Error.NotFound(new ResourceRef("Thing", "missing")));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization", "actor-vary headers must be emitted on cacheable failures, not just success");
    }

    [Fact]
    public async Task VaryForActor_applies_on_WriteOutcome_failure_path_too()
    {
        // Same partitioning requirement for the WriteOutcome failure path.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var r = Result.Fail<WriteOutcome<Thing>>(new Error.Conflict(null, "duplicate"));

        await r.ToHttpResponse<Thing>(o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization");
    }

    [Fact]
    public async Task VaryForActor_throws_fail_closed_on_failure_path_when_provider_unconfigured()
    {
        // The fail-closed validation must run before the failure response is written, so
        // misconfiguration surfaces with a clear error rather than silently shipping a
        // 404/403 that can pollute a cache across actors.
        var ctx = NewContext(provider: null);
        var r = Result.Fail<Thing>(new Error.NotFound(new ResourceRef("Thing", "missing")));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VaryForActor_applies_on_Error_ToHttpResponse_via_TrellisErrorOnlyResult()
    {
        // Standalone Error.ToHttpResponse(...) goes through TrellisErrorOnlyResult, which
        // builds with the non-generic HttpResponseOptionsBuilder. VaryForActor() on that
        // builder must also fire so cacheable error endpoints (e.g. dedicated
        // /problem-details responses keyed off actor visibility) partition correctly.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var error = new Error.Forbidden("policy-1", new ResourceRef("Thing", "1"));

        await error.ToHttpResponse(o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization");
    }

    [Fact]
    public async Task VaryForActor_applies_on_paged_success_path()
    {
        // The Result<Page<T>>.ToHttpResponse(...) overload accepts the same builder shape
        // as the non-paginated overloads. VaryForActor() on the paginated success path
        // must apply or it would silently no-op for cursor-paginated list endpoints (the
        // most common cacheable shape).
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var page = new Page<Thing>([new Thing(1, "a"), new Thing(2, "b")], Next: null, Previous: null, RequestedLimit: 50, AppliedLimit: 50);
        var r = Result.Ok(page);

        await r.ToHttpResponse(
            nextUrlBuilder: (cursor, limit) => $"/items?cursor={cursor}&limit={limit}",
            body: t => t,
            configure: o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization", "VaryForActor() must apply to the paged success path");
    }

    [Fact]
    public async Task VaryForActor_applies_on_paged_failure_path()
    {
        // Paged failure path goes through TrellisErrorOnlyResult; VaryForActor must
        // propagate from the Page<T> builder options into the error-only result.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        var r = Result.Fail<Page<Thing>>(new Error.NotFound(new ResourceRef("ThingList", "missing")));

        await r.ToHttpResponse(
            nextUrlBuilder: (cursor, limit) => $"/items?cursor={cursor}&limit={limit}",
            body: t => t,
            configure: o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization", "VaryForActor() must apply to the paged failure path too");
    }

    [Fact]
    public async Task VaryForActor_failure_message_names_inner_provider_when_caching_wraps_a_provider_without_capability()
    {
        // Critical UX detail: when a CachingActorProvider wraps a custom IActorProvider that
        // has not implemented IProvideActorVaryHeaders, the fail-closed message must point
        // at the inner provider (the one the consumer needs to fix), not the caching
        // wrapper. The IDecoratingActorProvider unwrap path makes this work.
        var inner = new ProviderWithoutVaryCapability();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var caching = new CachingActorProvider(inner, httpContextAccessor);
        var ctx = NewContext(caching);
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain(typeof(ProviderWithoutVaryCapability).FullName!,
            "the diagnostic must name the underlying provider that needs IProvideActorVaryHeaders, not the caching wrapper");
        ex.Message.Should().NotContain(nameof(CachingActorProvider),
            "the wrapper itself isn't the remediation site");
    }

    private sealed class SelfReferencingDecoratorProvider : IActorProvider, IDecoratingActorProvider, IProvideActorVaryHeaders
    {
        private static readonly System.Collections.Frozen.FrozenSet<string> EmptyPermissions =
            System.Collections.Frozen.FrozenSet<string>.Empty;

        public IReadOnlyCollection<string> VaryByHeaders { get; } = [];
        IActorProvider IDecoratingActorProvider.Inner => this;
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe.From(Actor.Create("test-actor", EmptyPermissions)));
    }

    [Fact]
    public async Task VaryForActor_throws_on_self_referencing_decorator_chain()
    {
        // Critical fail-closed guard: a malicious or accidentally-cyclic
        // IDecoratingActorProvider.Inner that returns itself would otherwise loop forever
        // inside AppendActorVaryHeaders' unwrap. The depth bound surfaces it as an
        // actionable error rather than a hung request thread.
        var ctx = NewContext(new SelfReferencingDecoratorProvider());
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("decorator").And.Contain("cycle");
    }

    [Fact]
    public async Task VaryForActor_unwraps_multi_level_decorator_chain_to_innermost_provider()
    {
        // Legitimate composition: someone wraps a CachingActorProvider in another caching /
        // diagnostic decorator. The unwrap loop must traverse the whole chain and name the
        // innermost provider in fail-closed diagnostics, not an intermediate wrapper.
        var inner = new ProviderWithoutVaryCapability();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var firstCache = new CachingActorProvider(inner, httpContextAccessor);
        var outerCache = new CachingActorProvider(firstCache, httpContextAccessor);
        var ctx = NewContext(outerCache);
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain(typeof(ProviderWithoutVaryCapability).FullName!,
            "the diagnostic must traverse every IDecoratingActorProvider hop and name the innermost provider that needs IProvideActorVaryHeaders");
    }

    [Fact]
    public async Task VaryForActor_applies_to_304_NotModified_response()
    {
        // 304 responses are cacheable and partition by actor too — a successful conditional
        // request from actor A must not return 304 to actor B who has never made a matching
        // request. VaryForActor must apply before the precondition evaluation branch.
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"abc123\"";
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o
            .WithETag(_ => "abc123")
            .EvaluatePreconditions()
            .VaryForActor()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(304);
        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization",
            "304 Not Modified must carry actor-vary headers to partition cached conditional-request responses by actor");
    }

    [Fact]
    public async Task VaryForActor_applies_to_412_PreconditionFailed_response()
    {
        // 412 from a failed If-Match is a typical concurrency-control response. While 412s
        // aren't commonly cached, the fail-closed validation must still run (a 412 emitted
        // without the right Vary on a cache-eligible URL could still pollute if some
        // intermediate caches the negative result).
        var ctx = NewContext(new TestActorProvider(["Authorization"]));
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"wrong-etag\"";
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o
            .WithETag(_ => "correct-etag")
            .EvaluatePreconditions()
            .VaryForActor()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(412);
        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization");
    }

    /// <summary>
    /// Non-delegating decorator: implements IDecoratingActorProvider (so the chain unwraps)
    /// but deliberately does NOT implement IProvideActorVaryHeaders. Realistic shape for
    /// cross-cutting wrappers (logging, metrics) that have nothing to say about how the
    /// actor maps to HTTP request headers.
    /// </summary>
    private sealed class LoggingDecoratorProvider(IActorProvider inner) : IActorProvider, IDecoratingActorProvider
    {
        public IActorProvider Inner => inner;
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => inner.GetCurrentActorAsync(cancellationToken);
    }

    /// <summary>
    /// Decorator that legitimately augments the inner provider's Vary headers (e.g.
    /// tenant-scoping wrapper that adds X-Tenant). Implements BOTH the unwrap interface
    /// and IProvideActorVaryHeaders so its outer set wins.
    /// </summary>
    private sealed class HeaderAugmentingDecoratorProvider(IActorProvider inner)
        : IActorProvider, IDecoratingActorProvider, IProvideActorVaryHeaders
    {
        public IActorProvider Inner => inner;
        public IReadOnlyCollection<string> VaryByHeaders { get; } = ["Authorization", "X-Tenant"];
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => inner.GetCurrentActorAsync(cancellationToken);
    }

    [Fact]
    public async Task VaryForActor_uses_inner_capability_when_decorator_does_not_implement_IProvideActorVaryHeaders()
    {
        // Realistic shape: a non-delegating decorator (logging/metrics) wraps a real provider
        // that DOES implement IProvideActorVaryHeaders. The framework must unwrap past the
        // decorator and use the inner provider's headers, not throw "decorator doesn't
        // implement IProvideActorVaryHeaders". The wrapper has nothing to say about HTTP
        // headers; the inner provider has the contract.
        var ctx = NewContext(new LoggingDecoratorProvider(new TestActorProvider(["Authorization"])));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization",
            "the framework must walk past IDecoratingActorProvider wrappers without IProvideActorVaryHeaders and read the inner provider's headers");
    }

    [Fact]
    public async Task VaryForActor_outermost_capability_in_chain_wins_so_decorators_can_augment_headers()
    {
        // Legitimate composition: a tenant-scoping decorator adds X-Tenant alongside the
        // inner bearer provider's Authorization. The outer wrapper's IProvideActorVaryHeaders
        // implementation is the authoritative one (it knows the full set required for cache
        // partitioning given its own behavior), not the inner provider's. The framework
        // honors the outermost capability in the chain.
        var ctx = NewContext(new HeaderAugmentingDecoratorProvider(new TestActorProvider(["Authorization"])));
        var r = Result.Ok(new Thing(1, "x"));

        await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Authorization").And.Contain("X-Tenant",
            "the outermost IProvideActorVaryHeaders in the decorator chain is authoritative; both Authorization (from the augmenting wrapper) and X-Tenant (added by the wrapper) must be emitted");
    }

    [Fact]
    public async Task VaryForActor_fails_closed_naming_innermost_when_no_layer_implements_capability()
    {
        // Non-delegating decorator wrapping a custom provider that ALSO lacks the
        // capability. Walk the entire chain, find no IProvideActorVaryHeaders anywhere,
        // and surface the innermost provider as the remediation site (that's where the
        // capability should go).
        var ctx = NewContext(new LoggingDecoratorProvider(new ProviderWithoutVaryCapability()));
        var r = Result.Ok(new Thing(1, "x"));

        var act = async () => await r.ToHttpResponse(t => t, o => o.VaryForActor()).ExecuteAsync(ctx);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain(typeof(ProviderWithoutVaryCapability).FullName!,
            "the diagnostic must name the innermost provider when no layer in the chain implements IProvideActorVaryHeaders");
        ex.Message.Should().NotContain(nameof(LoggingDecoratorProvider),
            "intermediate non-capability wrappers are not the remediation site");
    }
}