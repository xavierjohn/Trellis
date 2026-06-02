namespace Trellis.Asp.Tests;

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.Asp;
using Xunit;

/// <summary>
/// Tests for synthesising <c>ProblemDetails.Instance</c> from the failing
/// <see cref="ResourceRef"/> when the request URL does not already identify the resource.
/// The original request URL is preserved under <c>Extensions["request"]</c>.
/// </summary>
public sealed class ResponseFailureWriterResourceRefInstanceTests
{
    private static DefaultHttpContext NewContext(
        string path = "/api/orders",
        string? query = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        configureServices?.Invoke(services);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Path = path;
        if (query is not null)
            ctx.Request.QueryString = new QueryString(query);
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

    private sealed class FakeLogger : ILogger
    {
        public int WarningCount { get; private set; }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                WarningCount++;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed class FakeLoggerProvider(FakeLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

    private sealed record T(int Id);

    private static async Task<JsonDocument> ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NotFound_with_id_not_in_url_synthesizes_instance_from_resource_ref()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/customers/abc-123");
        body.RootElement.GetProperty("request").GetString().Should().Be("/api/orders");
    }

    [Fact]
    public async Task NotFound_with_id_already_in_url_keeps_request_url_as_instance()
    {
        var ctx = NewContext(path: "/api/customers/abc-123");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/customers/abc-123");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task NotFound_with_null_id_does_not_synthesize()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.NotFound(new ResourceRef("Customer")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_with_resource_id_synthesizes_instance()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.Conflict(ResourceRef.For("Order", "ord-9"), "duplicate_key"));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/ord-9");
        body.RootElement.GetProperty("request").GetString().Should().Be("/api/orders");
    }

    [Fact]
    public async Task Conflict_without_resource_keeps_request_url()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.Conflict(null, "version_conflict"));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_with_resource_id_synthesizes_instance()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.Forbidden("Order.Read", ResourceRef.For("Order", "ord-9")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/ord-9");
    }

    [Fact]
    public async Task Gone_with_resource_id_synthesizes_instance()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.Gone(ResourceRef.For("Order", "ord-9")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/ord-9");
    }

    [Fact]
    public async Task InvariantViolation_with_resource_id_synthesizes_instance()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.InvariantViolation("amount.negative", ResourceRef.For("Order", "ord-9")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/ord-9");
    }

    [Fact]
    public async Task TransportFault_PreconditionFailed_with_resource_id_synthesizes_instance()
    {
        var ctx = NewContext(path: "/api/orders");
        var fault = new HttpError.PreconditionFailed(ResourceRef.For("Order", "ord-9"), PreconditionKind.IfMatch);
        var r = Result.Fail<T>(new Error.TransportFault(fault));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/ord-9");
    }

    [Fact]
    public async Task Synthesis_uses_attribute_override_via_registry_for_irregular_plurals()
    {
        var ctx = NewContext(
            path: "/api/orders",
            configureServices: s => s.AddResourceCollectionName("Person", "people"));

        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Person", "p-1")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/people/p-1");
    }

    [Fact]
    public async Task Synthesis_disabled_via_option_keeps_request_url()
    {
        var ctx = NewContext(
            path: "/api/orders",
            configureServices: s => s.AddTrellisAsp(o => o.SynthesizeProblemDetailsInstanceFromResourceRef = false));

        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Id_matches_path_segment_exactly_not_substring()
    {
        // Id "1" should NOT collide with "v1" path segment — segment-aware match.
        var ctx = NewContext(path: "/api/v1/orders");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Order", "1")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/orders/1");
        body.RootElement.GetProperty("request").GetString().Should().Be("/api/v1/orders");
    }

    [Fact]
    public async Task Id_matches_query_value_keeps_request_url()
    {
        var ctx = NewContext(path: "/api/orders", query: "?customerId=abc-123");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders?customerId=abc-123");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Id_with_url_unsafe_chars_is_percent_encoded()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Document", "a/b?c")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/documents/" + Uri.EscapeDataString("a/b?c"));
    }

    [Fact]
    public async Task Lowercase_percent_escape_in_path_matches_raw_id()
    {
        // RFC 3986 §6.2.2.1: percent-encoded triplets are case-insensitive. A URL containing
        // /a%2fb (lowercase hex) must match the raw id "a/b" so synthesis does not duplicate
        // the resource in /documents/a%2Fb.
        var ctx = NewContext(path: "/api/documents/a%2fb");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Document", "a/b")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/documents/a%2fb");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Weird_percent_escapes_in_path_do_not_throw_and_synthesis_still_runs()
    {
        // Instance synthesis is best-effort: a path that looks like a malformed percent-escape
        // must never surface as a UriFormatException / 500 from the writer. ASP.NET's
        // Request.GetEncodedPathAndQuery() normalizes the path (bare '%' becomes '%25'), and
        // .NET's current Uri.UnescapeDataString is permissive, so this end-to-end test passes
        // today even without the defensive catch — it locks the contract for future runtimes
        // that may tighten the parser.
        var ctx = NewContext(path: "/api/documents/foo%ZZbar");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Document", "abc")));

        var act = async () => await r.ToHttpResponse(t => t).ExecuteAsync(ctx);
        await act.Should().NotThrowAsync();

        ctx.Response.StatusCode.Should().Be(404);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/documents/abc");
        body.RootElement.TryGetProperty("request", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Form_encoded_plus_in_query_matches_id_with_space()
    {
        // ASP.NET Core's query parser treats '+' as space (form-encoded). A request like
        // ?title=a+b therefore identifies the resource with id "a b" and synthesis should
        // not fire.
        var ctx = NewContext(path: "/api/documents", query: "?title=a+b");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Document", "a b")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/documents?title=a+b");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Key_only_query_pair_does_not_suppress_instance_synthesis()
    {
        // A query string like "?abc-123" carries a key-only parameter (no value). It does
        // NOT identify a resource by id, so the id-in-URL check must skip it and synthesis
        // must still fire.
        var ctx = NewContext(path: "/api/orders", query: "?abc-123");
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/customers/abc-123");
        body.RootElement.GetProperty("request").GetString().Should().Be("/api/orders?abc-123");
    }

    [Fact]
    public async Task Registry_activation_failure_falls_back_to_request_url_not_500()
    {
        // The ResourceCollectionNameRegistry ctor validates supplied overrides and throws
        // on misconfiguration (e.g., duplicate type with different collection names). DI
        // surfaces that as an activation exception when the singleton is first resolved.
        // Synthesis must remain best-effort and fall back to the request URL — never turn
        // a legitimate 404 into a 500.
        var ctx = NewContext(
            path: "/api/orders",
            configureServices: s => s.AddSingleton<ResourceCollectionNameRegistry>(_ =>
                throw new InvalidOperationException("Simulated registry activation failure.")));
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrySynthesizeInstance_RegistryThrows_FallsBackAndLogsOnce()
    {
        ResponseFailureWriter.ResetDiagnosticThrottlesForTests();
        var fakeLogger = new FakeLogger();

        using var firstBody = await ExecuteWithThrowingRegistryAsync(fakeLogger);
        firstBody.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        firstBody.RootElement.TryGetProperty("request", out _).Should().BeFalse();
        fakeLogger.WarningCount.Should().Be(1);

        using var secondBody = await ExecuteWithThrowingRegistryAsync(fakeLogger);
        secondBody.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        secondBody.RootElement.TryGetProperty("request", out _).Should().BeFalse();
        fakeLogger.WarningCount.Should().Be(1, "second call must NOT log again");
    }

    private static async Task<JsonDocument> ExecuteWithThrowingRegistryAsync(FakeLogger fakeLogger)
    {
        var ctx = NewContext(
            path: "/api/orders",
            configureServices: s =>
            {
                s.AddSingleton<ResourceCollectionNameRegistry>(_ =>
                    throw new InvalidOperationException("Simulated registry activation failure."));
                s.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddProvider(new FakeLoggerProvider(fakeLogger));
                });
            });
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For("Customer", "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        return await ReadBody(ctx);
    }

    [Fact]
    public async Task Single_child_aggregate_does_not_synthesize_instance_from_child()
    {
        // The aggregate carries no ResourceRef of its own, so synthesis NEVER promotes a
        // child's ResourceRef — even when there is only one child. Locking down this
        // contract prevents surprising special-casing.
        var ctx = NewContext(path: "/api/orders");
        var agg = new Error.Aggregate(new Error.NotFound(ResourceRef.For("Customer", "c-1")));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Aggregate_outer_does_not_synthesize_instance_from_first_child()
    {
        // Aggregate carries no ResourceRef of its own; children are projected into errors[].
        var ctx = NewContext(path: "/api/orders");
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Customer", "c-1")),
            new Error.InvariantViolation("rule.broken", ResourceRef.For("Order", "o-9")));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Malformed_resource_ref_with_empty_type_keeps_request_url()
    {
        var ctx = NewContext(path: "/api/orders");
        var r = Result.Fail<T>(new Error.NotFound(new ResourceRef(string.Empty, "abc-123")));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/orders");
        body.RootElement.TryGetProperty("request", out _).Should().BeFalse();
    }

    [Fact]
    public void Registry_resolves_naive_plural_when_no_overrides_registered()
    {
        var registry = new ResourceCollectionNameRegistry();

        registry.Resolve("Order").Should().Be("orders");
        registry.Resolve("UserAccount").Should().Be("useraccounts");
    }

    [Fact]
    public void Registry_resolves_override_case_insensitively()
    {
        var registry = new ResourceCollectionNameRegistry(new[]
        {
            new ResourceCollectionNameOverride("Person", "people"),
        });

        registry.Resolve("Person").Should().Be("people");
        registry.Resolve("person").Should().Be("people");
        registry.Resolve("PERSON").Should().Be("people");
        registry.Resolve("Order").Should().Be("orders");
    }

    [Fact]
    public void Registry_throws_when_duplicate_overrides_disagree()
    {
        var overrides = new[]
        {
            new ResourceCollectionNameOverride("Person", "people"),
            new ResourceCollectionNameOverride("Person", "staff"),
        };

        var act = () => new ResourceCollectionNameRegistry(overrides);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate*Person*");
    }

    [Fact]
    public void Registry_coalesces_identical_overrides()
    {
        var overrides = new[]
        {
            new ResourceCollectionNameOverride("Person", "people"),
            new ResourceCollectionNameOverride("Person", "people"),
        };

        var registry = new ResourceCollectionNameRegistry(overrides);
        registry.Resolve("Person").Should().Be("people");
    }

    [Fact]
    public void Attribute_throws_on_unsafe_collection_name()
    {
        var actSpace = () => new ResourceCollectionNameAttribute("peo ple");
        actSpace.Should().Throw<ArgumentException>();

        var actSlash = () => new ResourceCollectionNameAttribute("peo/ple");
        actSlash.Should().Throw<ArgumentException>();

        var actEmpty = () => new ResourceCollectionNameAttribute(string.Empty);
        actEmpty.Should().Throw<ArgumentException>();

        var actPercent = () => new ResourceCollectionNameAttribute("peo%2Fple");
        actPercent.Should().Throw<ArgumentException>();

        var actPlus = () => new ResourceCollectionNameAttribute("peo+ple");
        actPlus.Should().Throw<ArgumentException>();

        var actColon = () => new ResourceCollectionNameAttribute("ns:people");
        actColon.Should().Throw<ArgumentException>();

        var actQuery = () => new ResourceCollectionNameAttribute("people?x");
        actQuery.Should().Throw<ArgumentException>();

        var actFragment = () => new ResourceCollectionNameAttribute("people#x");
        actFragment.Should().Throw<ArgumentException>();

        var actNull = () => new ResourceCollectionNameAttribute(null!);
        actNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSafePathSegment_accepts_unreserved_and_rejects_everything_else()
    {
        ResourceCollectionNameAttribute.IsSafePathSegment("people").Should().BeTrue();
        ResourceCollectionNameAttribute.IsSafePathSegment("user-accounts").Should().BeTrue();
        ResourceCollectionNameAttribute.IsSafePathSegment("v2.objects").Should().BeTrue();
        ResourceCollectionNameAttribute.IsSafePathSegment("a_b~c").Should().BeTrue();
        ResourceCollectionNameAttribute.IsSafePathSegment("items42").Should().BeTrue();

        ResourceCollectionNameAttribute.IsSafePathSegment(null).Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment(string.Empty).Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment("   ").Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment("peo%2Fple").Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment("peo+ple").Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment("ns:items").Should().BeFalse();
        ResourceCollectionNameAttribute.IsSafePathSegment("café").Should().BeFalse();
    }

    [Fact]
    public void AddResourceCollectionNames_params_overload_scans_each_assembly()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionNames(
            typeof(Sample.AnnotatedPerson).Assembly,
            typeof(Sample.AnnotatedPerson).Assembly);

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ResourceCollectionNameRegistry>();

        registry.Resolve("AnnotatedPerson").Should().Be("staff");
    }

    [Fact]
    public void AddResourceCollectionName_typed_overload_uses_FormatTypeName()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Sample.Person>("people");

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ResourceCollectionNameRegistry>();

        registry.Resolve("Person").Should().Be("people");
    }

    [Fact]
    public void AddResourceCollectionNames_assembly_scan_picks_up_attribute()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionNames(typeof(Sample.AnnotatedPerson).Assembly);

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ResourceCollectionNameRegistry>();

        registry.Resolve("AnnotatedPerson").Should().Be("staff");
    }
}
