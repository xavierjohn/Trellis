namespace Trellis.Asp.ApiVersioning.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Pins the warn-on-first-skip diagnostic behavior of
/// <see cref="SilentVersionInjectionDiagnostic"/> as exercised through the two
/// <c>.WithVersionedRoute()</c> overloads. Each test stands up a minimal ASP.NET Core
/// host that captures logger output via an in-memory <see cref="ILoggerProvider"/>,
/// drives one or more requests, and asserts the captured warnings against the
/// (endpoint, AppDomain) de-duplication contract.
/// </summary>
/// <remarks>
/// <para>
/// Each test calls <see cref="SilentVersionInjectionDiagnostic.ResetForTests"/> in the
/// constructor — the diagnostic's seen-set is process-wide (the contract is "warn once
/// per (endpoint, AppDomain)") so test cases must clear shared state before observing
/// first-skip emission. Unique route templates per test minimise collision with
/// sibling test classes (xUnit v3 runs classes in parallel by default).
/// </para>
/// <para>
/// The class also opts into a dedicated non-parallelised xUnit collection so that any
/// future test class touching the same diagnostic's static seen-set can be added to the
/// collection (via <c>[Collection("SilentVersionInjectionDiagnosticState")]</c>) and
/// share serialised execution; in-class tests run sequentially regardless. Without
/// this, a parallel class that resets the seen-set could clear an entry another class
/// was about to assert against.
/// </para>
/// </remarks>
[Collection("SilentVersionInjectionDiagnosticState")]
[CollectionDefinition("SilentVersionInjectionDiagnosticState", DisableParallelization = true)]
public sealed class SilentVersionInjectionDiagnosticTests
{
    public SilentVersionInjectionDiagnosticTests() => SilentVersionInjectionDiagnostic.ResetForTests();

    [Fact]
    public async Task Versioned_host_with_AddApiVersioning_logs_no_warning()
    {
        // Sanity guard: when the host correctly registered AddApiVersioning and the
        // endpoint carries [ApiVersion(...)], the diagnostic must stay silent. This is the
        // steady-state production case for a versioned API; a warning here would be noise.
        var capture = new CapturingLoggerProvider();
        using var host = CreateVersionedHost(capture);
        using var client = host.GetTestClient();

        var resp = await client.PostAsync(
            $"/diag-versioned/items?api-version={DiagApiVersionV1}",
            JsonContent(),
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        CaptureWarnings(capture).Should().BeEmpty();
    }

    [Fact]
    public async Task Unversioned_host_with_WithVersionedRoute_chain_logs_warning_once_per_endpoint()
    {
        // The mid-migration regression target: the host did not call AddApiVersioning(...)
        // but the controller still chains .WithVersionedRoute(). First request emits the
        // warning; second request to the same endpoint stays silent (per-endpoint dedup).
        var capture = new CapturingLoggerProvider();
        using var host = CreateUnversionedHost<DiagUnversionedPerRequestController>(capture);
        using var client = host.GetTestClient();

        var first = await client.PostAsync("/diag-warn-once/items", JsonContent(), TestContext.Current.CancellationToken);
        var second = await client.PostAsync("/diag-warn-once/items", JsonContent(), TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var warnings = CaptureWarnings(capture);
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain(".WithVersionedRoute()");
        warnings[0].Should().Contain("ApiVersionMetadata");
        warnings[0].Should().Contain("AddApiVersioning");
        warnings[0].Should().Contain("FailFastOnSilentVersionInjection");
        // The endpoint key MUST be interpolated into the message — a broken LoggerMessage
        // placeholder ({EndpointKey} without its closing brace, etc.) would render as raw
        // template text and the endpoint identifier would never reach the log line.
        warnings[0].Should().Contain("DiagUnversionedPerRequestController.Post");
    }

    [Fact]
    public async Task Unversioned_host_warns_per_distinct_endpoint()
    {
        // Two endpoints on the same controller (and host) → two distinct warnings.
        // Confirms the de-dup key is per-endpoint, not per-host or per-controller.
        var capture = new CapturingLoggerProvider();
        using var host = CreateUnversionedHost<DiagUnversionedMultiEndpointController>(capture);
        using var client = host.GetTestClient();

        var r1 = await client.PostAsync("/diag-multi/alpha", JsonContent(), TestContext.Current.CancellationToken);
        var r2 = await client.PostAsync("/diag-multi/beta", JsonContent(), TestContext.Current.CancellationToken);

        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        r2.StatusCode.Should().Be(HttpStatusCode.Created);

        var warnings = CaptureWarnings(capture);
        warnings.Should().HaveCount(2);
        warnings.Should().Contain(w => w.Contains("DiagUnversionedMultiEndpointController.PostAlpha", StringComparison.Ordinal));
        warnings.Should().Contain(w => w.Contains("DiagUnversionedMultiEndpointController.PostBeta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explicit_pin_overload_on_unversioned_host_also_logs_warning()
    {
        // The pinned overload registers its own resolver delegate that ignores the
        // per-request resolution path. The diagnostic must still fire — silently dropping
        // an explicit pin is exactly the kind of mid-migration regression the warning
        // exists to surface.
        var capture = new CapturingLoggerProvider();
        using var host = CreateUnversionedHost<DiagUnversionedPinnedController>(capture);
        using var client = host.GetTestClient();

        var resp = await client.PostAsync("/diag-warn-pinned/items", JsonContent(), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var warnings = CaptureWarnings(capture);
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain(".WithVersionedRoute()");
    }

    [Fact]
    public async Task Opt_in_fail_fast_throws_on_every_request_not_just_first()
    {
        // FailFastOnSilentVersionInjection bypasses de-duplication so each offending
        // request fails. "Fail once, then succeed silently" would defeat the opt-in's
        // purpose (catch the misconfiguration in test runs / non-prod). TestServer
        // surfaces the InvalidOperationException directly out of SendAsync rather than
        // wrapping it as a 500 response.
        var capture = new CapturingLoggerProvider();
        using var host = CreateUnversionedHost<DiagUnversionedFailFastController>(
            capture,
            configureAsp: opts => opts.FailFastOnSilentVersionInjection = true);
        using var client = host.GetTestClient();

        Func<Task> first = () => client.PostAsync("/diag-fail-fast/items", JsonContent(), TestContext.Current.CancellationToken);
        Func<Task> second = () => client.PostAsync("/diag-fail-fast/items", JsonContent(), TestContext.Current.CancellationToken);

        (await first.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*WithVersionedRoute*FailFastOnSilentVersionInjection*");
        // The second call must also throw — proves the fail-fast path is not silenced
        // by the de-duplication seen-set after the first failure.
        await second.Should().ThrowAsync<InvalidOperationException>();

        // No warning is logged on the fail-fast path — the diagnostic throws before the
        // logging branch runs.
        CaptureWarnings(capture).Should().BeEmpty();
    }

    [Fact]
    public async Task ApiVersionNeutral_endpoint_logs_no_warning()
    {
        // Regression guard: [ApiVersionNeutral] endpoints have ApiVersionMetadata attached
        // (with IsApiVersionNeutral = true) and are an *intentional* skip case. The
        // diagnostic must NOT fire for them — only the missing-metadata case warrants the
        // warning. Requires AddApiVersioning on the host so the [ApiVersionNeutral]
        // attribute produces metadata.
        var capture = new CapturingLoggerProvider();
        using var host = CreateVersionedHost(capture);
        using var client = host.GetTestClient();

        var resp = await client.PostAsync(
            $"/diag-neutral/items?api-version={DiagApiVersionV1}",
            JsonContent(),
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        CaptureWarnings(capture).Should().BeEmpty();
    }

    [Fact]
    public async Task PageUrl_on_unversioned_host_logs_no_warning()
    {
        // Boundary guard: HttpContext.PageUrl(...) is INTENTIONALLY out of scope for this
        // diagnostic. PageUrl is an explicit per-call API whose return value the caller
        // inspects directly (the next-page URL); silent api-version drops on PageUrl are a
        // visible degradation the caller can see. .WithVersionedRoute() is the one that
        // injects into a Location header the caller never inspects, so the warning is
        // scoped to that case only. If a future refactor accidentally wires the diagnostic
        // into PageUrl as well, this test fails and the boundary is restored deliberately.
        var capture = new CapturingLoggerProvider();
        using var host = CreateUnversionedHost<DiagUnversionedPageUrlController>(capture);
        using var client = host.GetTestClient();

        var resp = await client.GetAsync(
            "/diag-pageurl-unversioned/items",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        CaptureWarnings(capture).Should().BeEmpty();
    }

    public const string DiagApiVersionV1 = "2027-01-01";

    private static IHost CreateVersionedHost(CapturingLoggerProvider capture)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.AddProvider(capture);
                    lb.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().ConfigureApplicationPartManager(apm =>
                    {
                        apm.FeatureProviders.Clear();
                        apm.FeatureProviders.Add(new SingleControllerFeatureProvider(
                            typeof(DiagVersionedController),
                            typeof(DiagVersionedNeutralController)));
                    });
                    s.AddApiVersioning(o =>
                    {
                        o.ApiVersionReader = new QueryStringApiVersionReader("api-version");
                        o.AssumeDefaultVersionWhenUnspecified = true;
                        o.DefaultApiVersion = new ApiVersion(new DateOnly(2027, 1, 1));
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static IHost CreateUnversionedHost<TController>(
        CapturingLoggerProvider capture,
        Action<TrellisAspOptions>? configureAsp = null)
        where TController : ControllerBase
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.AddProvider(capture);
                    lb.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices(s =>
                {
                    if (configureAsp is null)
                        s.AddTrellisAspWithScalarValidation();
                    else
                        s.AddTrellisAspWithScalarValidation(configureAsp);

                    s.AddControllers().ConfigureApplicationPartManager(apm =>
                    {
                        apm.FeatureProviders.Clear();
                        apm.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(TController)));
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static StringContent JsonContent() =>
        new("{}", System.Text.Encoding.UTF8, "application/json");

    private static List<string> CaptureWarnings(CapturingLoggerProvider capture) =>
        capture.Entries
            .Where(e =>
                e.Level == LogLevel.Warning &&
                e.Category == SilentVersionInjectionDiagnostic.LoggerCategory)
            .Select(e => e.Message)
            .ToList();

    private sealed class SingleControllerFeatureProvider : ControllerFeatureProvider
    {
        private readonly HashSet<Type> _allowed;
        public SingleControllerFeatureProvider(params Type[] allowed) => _allowed = [.. allowed];
        protected override bool IsController(System.Reflection.TypeInfo typeInfo) => _allowed.Contains(typeInfo.AsType());
    }

    private sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentBag<CapturedLogEntry> _entries = new();
        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);
        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentBag<CapturedLogEntry> _sink;
            public CapturingLogger(string category, ConcurrentBag<CapturedLogEntry> sink)
            {
                _category = category;
                _sink = sink;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _sink.Add(new CapturedLogEntry(_category, logLevel, formatter(state, exception)));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}

#region Diagnostic test controllers

[ApiController]
[ApiVersion(SilentVersionInjectionDiagnosticTests.DiagApiVersionV1)]
[Route("diag-versioned/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagVersionedController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Diag_Versioned_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedDiag> Post() =>
        Result.Ok(new CreatedDiag(1))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_Versioned_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[ApiVersionNeutral]
[Route("diag-neutral/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagVersionedNeutralController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Diag_Neutral_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedDiag> Post() =>
        Result.Ok(new CreatedDiag(2))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_Neutral_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[Route("diag-warn-once/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagUnversionedPerRequestController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Diag_WarnOnce_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedDiag> Post() =>
        Result.Ok(new CreatedDiag(3))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_WarnOnce_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[Route("diag-multi")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagUnversionedMultiEndpointController : ControllerBase
{
    [HttpGet("alpha/{id:int}", Name = "Diag_Multi_Alpha_GetById")]
    public IActionResult GetAlpha(int id) => Ok(new { id });

    [HttpGet("beta/{id:int}", Name = "Diag_Multi_Beta_GetById")]
    public IActionResult GetBeta(int id) => Ok(new { id });

    [HttpPost("alpha")]
    public ActionResult<CreatedDiag> PostAlpha() =>
        Result.Ok(new CreatedDiag(4))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_Multi_Alpha_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();

    [HttpPost("beta")]
    public ActionResult<CreatedDiag> PostBeta() =>
        Result.Ok(new CreatedDiag(5))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_Multi_Beta_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[Route("diag-warn-pinned/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagUnversionedPinnedController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Diag_WarnPinned_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedDiag> Post() =>
        Result.Ok(new CreatedDiag(6))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_WarnPinned_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute(new ApiVersion(new DateOnly(2027, 1, 1))))
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[Route("diag-fail-fast/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagUnversionedFailFastController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Diag_FailFast_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedDiag> Post() =>
        Result.Ok(new CreatedDiag(7))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Diag_FailFast_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedDiag>();
}

[ApiController]
[Route("diag-pageurl-unversioned/items")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class DiagUnversionedPageUrlController : ControllerBase
{
    [HttpGet(Name = "Diag_PageUrlUnversioned_List")]
    public Microsoft.AspNetCore.Http.IResult List([FromQuery] string? cursor)
    {
        var page = new Page<string>(
            Items: ["a", "b"],
            Next: new Cursor("next-cur"),
            Previous: null,
            RequestedLimit: 2,
            AppliedLimit: 2);
        return Result.Ok(page).ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Diag_PageUrlUnversioned_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: x => x);
    }
}

public sealed record CreatedDiag(int Id);

#endregion