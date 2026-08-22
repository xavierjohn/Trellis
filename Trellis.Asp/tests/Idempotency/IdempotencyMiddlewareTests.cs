namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Asp.Idempotency;

/// <summary>
/// Integration tests for the full <see cref="IdempotencyMiddleware"/> pipeline driven through
/// a <see cref="TestServer"/>. Pins the IETF Idempotency-Key contract end-to-end: opt-in via
/// <c>IdempotentAttribute</c>, replay verbatim, in-flight 409, body-mismatch
/// <c>idempotency.key_reused_with_different_body</c>, and request-body 413.
/// </summary>
public sealed class IdempotencyMiddlewareTests
{
    private const string KeyHeader = "Idempotency-Key";

    private static async Task<IHost> BuildHost(
        Action<IEndpointRouteBuilder>? configureEndpoints = null,
        Action<IdempotencyOptions>? configureOptions = null,
        Action<ILoggingBuilder>? configureLogging = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => configureLogging?.Invoke(logging))
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddTrellisIdempotency(configureOptions);
                    s.AddInMemoryIdempotencyStore();
                    configureServices?.Invoke(s);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseTrellisIdempotency();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/idempotent", async ctx =>
                        {
                            ctx.Response.StatusCode = 201;
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync("{\"order\":\"created\"}", ctx.RequestAborted);
                        }).WithMetadata(new IdempotentAttribute());

                        endpoints.MapPost("/passthrough", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("ok", ctx.RequestAborted);
                        });

                        endpoints.MapGet("/idempotent-get", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("get-ok", ctx.RequestAborted);
                        }).WithMetadata(new IdempotentAttribute());

                        configureEndpoints?.Invoke(endpoints);
                    });
                }));

        var host = await builder.StartAsync();
        return host;
    }

    private static IHostBuilder CreateIdempotencyOptionsHost(Action<IdempotencyOptions>? configureOptions = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(s => s.AddTrellisIdempotency(configureOptions));

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Endpoint_without_attribute_is_pass_through()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var content = JsonBody("{\"x\":1}");
        content.Headers.Add(KeyHeader, "abc");
        var response = await client.PostAsync("/passthrough", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task Method_outside_options_set_is_pass_through()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/idempotent-get");
        req.Headers.Add(KeyHeader, "k1");
        var response = await client.SendAsync(req, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("get-ok");
    }

    [Fact]
    public async Task Missing_header_returns_400_when_RequireKey_default_true()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var response = await client.PostAsync("/idempotent", JsonBody("{\"x\":1}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_required");
        body.Should().Contain("\"kind\":\"bad-request\"",
            "every failure response carries a top-level kind, whichever layer wrote it");
        body.Should().Contain("\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\"",
            "type is the status URI the response writer would resolve, not a hard-coded about:blank; "
            + "the same status must not describe itself two ways depending on which layer answered");
    }

    [Fact]
    public async Task Missing_header_is_pass_through_when_RequireKey_disabled()
    {
        using var host = await BuildHost(configureOptions: o => o.RequireKeyOnOptedInEndpoints = false);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/idempotent", JsonBody("{\"x\":1}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task First_request_executes_and_second_request_replays_verbatim()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/idempotent", first, TestContext.Current.CancellationToken);

        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        firstResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await firstResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("{\"order\":\"created\"}");

        var second = JsonBody("{\"x\":1}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/idempotent", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
        (await secondResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("{\"order\":\"created\"}");
    }

    [Fact]
    public async Task Reused_key_with_different_body_returns_mismatch_status()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(KeyHeader, key);
        await client.PostAsync("/idempotent", first, TestContext.Current.CancellationToken);

        var second = JsonBody("{\"x\":2}");
        second.Headers.Add(KeyHeader, key);
        var resp = await client.PostAsync("/idempotent", second, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_reused_with_different_body");
        body.Should().Contain("\"kind\":\"unprocessable-content\"",
            "every failure response carries a top-level kind, whichever layer wrote it");
    }

    [Fact]
    public async Task Invalid_key_returns_400_problem_details()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.TryAddWithoutValidation(KeyHeader, "bad key with space");
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_invalid");
    }

    [Fact]
    public async Task Key_too_long_returns_400()
    {
        using var host = await BuildHost(configureOptions: o => o.MaxKeyLength = 10);
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.Add(KeyHeader, new string('a', 20));
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_too_long");
    }

    [Fact]
    public async Task Body_exceeding_max_returns_413()
    {
        using var host = await BuildHost(configureOptions: o => o.MaxRequestBodyBytes = 16);
        var client = host.GetTestClient();

        var content = JsonBody(new string('z', 100));
        content.Headers.Add(KeyHeader, "k1");
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.request_body_too_large");
    }

    [Fact]
    public async Task UseTrellisIdempotency_throws_when_no_store_registered()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddTrellisIdempotency();
                })
                .Configure(app =>
                {
                    var act = () => app.UseTrellisIdempotency();
                    act.Should().Throw<InvalidOperationException>()
                        .WithMessage("*IIdempotencyStore*");
                }));

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UseTrellisIdempotency_throws_when_AddTrellisIdempotency_not_called()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s => s.AddRouting())
                .Configure(app =>
                {
                    var act = () => app.UseTrellisIdempotency();
                    act.Should().Throw<InvalidOperationException>()
                        .WithMessage("*AddTrellisIdempotency*");
                }));

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
    }

    public static TheoryData<string, Action<IdempotencyOptions>, string> InvalidIdempotencyOptions() => new()
    {
        { nameof(IdempotencyOptions.HeaderName), o => o.HeaderName = "", "must be set to a non-empty header name" },
        { nameof(IdempotencyOptions.HeaderName), o => o.HeaderName = "Bad Header", "must be a valid HTTP header name" },
        { nameof(IdempotencyOptions.ReplayHeaderName), o => o.ReplayHeaderName = "", "must be set to a non-empty header name" },
        { nameof(IdempotencyOptions.ReplayHeaderName), o => o.ReplayHeaderName = "Bad Header", "must be a valid HTTP header name" },
        { nameof(IdempotencyOptions.Ttl), o => o.Ttl = TimeSpan.Zero, "must be greater than TimeSpan.Zero" },
        { nameof(IdempotencyOptions.ReservationTimeout), o => o.ReservationTimeout = TimeSpan.Zero, "must be greater than TimeSpan.Zero" },
        { nameof(IdempotencyOptions.MaxKeyLength), o => o.MaxKeyLength = 0, "must be greater than 0" },
        { nameof(IdempotencyOptions.MaxRequestBodyBytes), o => o.MaxRequestBodyBytes = 0, "must be greater than 0" },
        { nameof(IdempotencyOptions.MaxResponseBodyBytes), o => o.MaxResponseBodyBytes = 0, "must be greater than 0" },
        { nameof(IdempotencyOptions.MismatchStatusCode), o => o.MismatchStatusCode = 200, "must be between 400 and 599" },
        { nameof(IdempotencyOptions.Methods), o => o.Methods.Clear(), "must contain at least one HTTP method" },
        { nameof(IdempotencyOptions.Methods), o => o.Methods.Add("BAD METHOD"), "must contain only valid HTTP method tokens" },
        { nameof(IdempotencyOptions.AdditionalFingerprintHeaders), o => o.AdditionalFingerprintHeaders.Add("Bad Header"), "must contain only valid HTTP header names" },
    };

    [Fact]
    public async Task AddTrellisIdempotency_DefaultOptions_HostStartsSuccessfully()
    {
        using var host = CreateIdempotencyOptionsHost().Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        options.HeaderName.Should().Be(KeyHeader);
    }

    [Fact]
    public async Task AddTrellisIdempotency_ValidOptions_HostStartsSuccessfully()
    {
        using var host = CreateIdempotencyOptionsHost(o =>
        {
            o.HeaderName = "X-Idempotency-Key";
            o.ReplayHeaderName = "X-Idempotency-Replayed";
            o.Ttl = TimeSpan.FromMinutes(5);
            o.ReservationTimeout = TimeSpan.FromSeconds(1);
            o.MaxKeyLength = 64;
            o.MaxRequestBodyBytes = 1024;
            o.MaxResponseBodyBytes = 2048;
            o.MismatchStatusCode = 409;
            o.RequireKeyOnOptedInEndpoints = false;
            o.IncludeSetCookieInSnapshot = true;
            o.Methods.Add(HttpMethod.Put.Method);
            o.AdditionalFingerprintHeaders.Add("Accept-Language");
        }).Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        options.HeaderName.Should().Be("X-Idempotency-Key");
        options.ReplayHeaderName.Should().Be("X-Idempotency-Replayed");
        options.AdditionalFingerprintHeaders.Should().Contain("Accept-Language");
    }

    [Theory]
    [MemberData(nameof(InvalidIdempotencyOptions))]
    public async Task AddTrellisIdempotency_InvalidOptions_ThrowsOptionsValidationException(
        string propertyName,
        Action<IdempotencyOptions> configureOptions,
        string messageFragment)
    {
        var act = async () =>
        {
            using var host = CreateIdempotencyOptionsHost(configureOptions).Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage($"*{propertyName}*{messageFragment}*");
    }

    [Fact]
    public void AddTrellisIdempotency_registers_DefaultIdempotencyScopeResolver_by_default()
    {
        var services = new ServiceCollection();
        services.AddTrellisIdempotency();
        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IIdempotencyScopeResolver>();

        resolver.Should().BeOfType<DefaultIdempotencyScopeResolver>();
    }

    [Fact]
    public void AddTrellisIdempotency_is_idempotent_for_marker_registration()
    {
        var services = new ServiceCollection();

        services.AddTrellisIdempotency();
        services.AddTrellisIdempotency();
        services.AddTrellisIdempotency();

        services
            .Count(d => d.ServiceType == typeof(IdempotencyServiceCollectionExtensions.IdempotencyMarker))
            .Should().Be(1);
    }

    [Fact]
    public async Task SetCookie_header_is_filtered_from_snapshot_by_default()
    {
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/with-cookie", async ctx =>
            {
                ctx.Response.StatusCode = 201;
                ctx.Response.Headers["Set-Cookie"] = "session=abc; Path=/";
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}", ctx.RequestAborted);
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/with-cookie", first, TestContext.Current.CancellationToken);
        firstResp.Headers.Contains("Set-Cookie").Should().BeTrue("the live response should still carry Set-Cookie");

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/with-cookie", second, TestContext.Current.CancellationToken);

        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
        secondResp.Headers.Contains("Set-Cookie").Should().BeFalse("replayed response must not re-issue cookies");
    }

    [Fact]
    public async Task SetCookie_header_is_included_in_snapshot_when_option_enabled()
    {
        using var host = await BuildHost(
            configureEndpoints: endpoints =>
                endpoints.MapPost("/with-cookie", async ctx =>
                {
                    ctx.Response.StatusCode = 201;
                    ctx.Response.Headers["Set-Cookie"] = "session=abc; Path=/";
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"ok\":true}", ctx.RequestAborted);
                }).WithMetadata(new IdempotentAttribute()),
            configureOptions: o => o.IncludeSetCookieInSnapshot = true);
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        await client.PostAsync("/with-cookie", first, TestContext.Current.CancellationToken);

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/with-cookie", second, TestContext.Current.CancellationToken);

        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
        secondResp.Headers.Contains("Set-Cookie").Should().BeTrue();
    }

    [Fact]
    public async Task Bodyless_204_response_is_snapshotted_and_replayed()
    {
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/no-content", ctx =>
            {
                ctx.Response.StatusCode = 204;
                return Task.CompletedTask;
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/no-content", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        firstResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse();

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/no-content", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
    }

    [Fact]
    public async Task Bodyless_201_with_Location_only_is_snapshotted_and_replayed()
    {
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/created-headers-only", ctx =>
            {
                ctx.Response.StatusCode = 201;
                ctx.Response.Headers["Location"] = "/orders/42";
                return Task.CompletedTask;
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/created-headers-only", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        firstResp.Headers.Location?.ToString().Should().Be("/orders/42");

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/created-headers-only", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResp.Headers.Location?.ToString().Should().Be("/orders/42");
        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
    }

    [Fact]
    public async Task ReplayHeaderName_option_is_honoured()
    {
        using var host = await BuildHost(configureOptions: o => o.ReplayHeaderName = "X-Trellis-Replayed");
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(KeyHeader, key);
        await client.PostAsync("/idempotent", first, TestContext.Current.CancellationToken);

        var second = JsonBody("{\"x\":1}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/idempotent", second, TestContext.Current.CancellationToken);

        secondResp.Headers.GetValues("X-Trellis-Replayed").Should().Contain("true");
        secondResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ServerErrorAbandonLog_SensitiveKey_RedactsRawKeyAndEmitsKeyHash()
    {
        const string rawKey = "acct-123-user-456-correlation";
        var expectedKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)))[..12].ToLowerInvariant();
        using var loggerProvider = new CapturingLoggerProvider();
        using var host = await BuildHost(
            configureEndpoints: endpoints =>
                endpoints.MapPost("/server-error-log", ctx =>
                {
                    ctx.Response.StatusCode = 503;
                    return Task.CompletedTask;
                }).WithMetadata(new IdempotentAttribute()),
            configureLogging: logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(loggerProvider);
                logging.SetMinimumLevel(LogLevel.Trace);
            });
        var client = host.GetTestClient();

        var request = JsonBody("{}");
        request.Headers.Add(KeyHeader, rawKey);
        var response = await client.PostAsync("/server-error-log", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var messages = loggerProvider.Messages;
        messages.Should().NotBeEmpty("the 5xx abandonment path logs the idempotency key correlation token");
        messages.Should().OnlyContain(message => !message.Contains(rawKey, StringComparison.Ordinal),
            "caller-supplied Idempotency-Key values can contain PII and must never be written to logs");
        messages.Should().Contain(message => message.Contains(expectedKeyHash, StringComparison.Ordinal),
            "operators still need a stable short hash to correlate idempotency log lines");
    }

    [Fact]
    public Task InvokeAsync_CompleteAsyncException_AbandonsReservationAndImmediateRetryReExecutes() =>
        AssertCompleteFailureAbandonsAndRetryReExecutes(
            () => new InvalidOperationException("complete failed"),
            "CompleteAsync failed");

    [Fact]
    public Task InvokeAsync_CompleteAsyncTimeout_AbandonsReservationAndImmediateRetryReExecutes() =>
        AssertCompleteFailureAbandonsAndRetryReExecutes(
            () => new OperationCanceledException("complete timed out"),
            "CompleteAsync timed out");

    [Fact]
    public async Task Server_error_response_is_abandoned_and_retry_re_executes_handler()
    {
        var executions = 0;
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/server-error", async ctx =>
            {
                Interlocked.Increment(ref executions);
                ctx.Response.StatusCode = 503;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsync("{\"detail\":\"transient\"}", ctx.RequestAborted);
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/server-error", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var second = JsonBody("{\"x\":1}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/server-error", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "transient 5xx responses must not be cached");
        secondResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse(
            "the retry must hit the handler again, not replay the original 5xx");
        executions.Should().Be(2, "the handler must execute on each retry of a 5xx outcome");
    }

    [Fact]
    public async Task Bodyless_server_error_is_abandoned_and_retry_re_executes_handler()
    {
        var executions = 0;
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/server-error-empty", ctx =>
            {
                Interlocked.Increment(ref executions);
                ctx.Response.StatusCode = 500;
                return Task.CompletedTask;
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/server-error-empty", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/server-error-empty", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        secondResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse(
            "5xx detection must work even when the handler never flushed the response body");
        executions.Should().Be(2);
    }

    [Fact]
    public async Task Client_error_response_is_cached_and_replayed()
    {
        var executions = 0;
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/client-error", async ctx =>
            {
                Interlocked.Increment(ref executions);
                ctx.Response.StatusCode = 422;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsync("{\"detail\":\"invalid input\"}", ctx.RequestAborted);
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/client-error", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var second = JsonBody("{\"x\":1}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/client-error", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true",
            "deterministic 4xx outcomes are still cached because retries will produce the same answer");
        executions.Should().Be(1, "the handler must run only once when a 4xx outcome was cached");
    }

    [Fact]
    public async Task Response_with_trailers_is_abandoned_and_retry_re_executes_handler()
    {
        var executions = 0;
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/with-trailers", async ctx =>
            {
                Interlocked.Increment(ref executions);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var trailers = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseTrailersFeature>();
                if (trailers is not null)
                {
                    trailers.Trailers["X-Trace"] = "abc";
                }

                await ctx.Response.WriteAsync("{\"ok\":true}", ctx.RequestAborted);
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        await client.PostAsync("/with-trailers", first, TestContext.Current.CancellationToken);

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/with-trailers", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse(
            "responses that wrote trailers cannot be replayed by the snapshot writer, so they must not be cached");
        executions.Should().Be(2, "the handler must execute on each retry when trailers were written");
    }

    [Fact]
    public async Task Scoped_store_registration_is_resolved_per_request()
    {
        var instanceCount = 0;
        var shared = new InMemoryIdempotencyStore(new IdempotencyOptions(), TimeProvider.System);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .UseDefaultServiceProvider(opts =>
                {
                    opts.ValidateScopes = true;
                    opts.ValidateOnBuild = true;
                })
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddTrellisIdempotency();
                    s.AddScoped<IIdempotencyStore>(_ =>
                    {
                        Interlocked.Increment(ref instanceCount);
                        return new DelegatingIdempotencyStore(shared);
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseTrellisIdempotency();
                    app.UseEndpoints(endpoints => endpoints.MapPost("/idempotent-scoped", async ctx =>
                    {
                        ctx.Response.StatusCode = 201;
                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.WriteAsync("{\"created\":true}", ctx.RequestAborted);
                    }).WithMetadata(new IdempotentAttribute()));
                }));

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        var client = host.GetTestClient();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, Guid.NewGuid().ToString());
        var firstResp = await client.PostAsync("/idempotent-scoped", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, Guid.NewGuid().ToString());
        var secondResp = await client.PostAsync("/idempotent-scoped", second, TestContext.Current.CancellationToken);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);

        instanceCount.Should().Be(2,
            "a scoped IIdempotencyStore registration must be resolved fresh per request via InvokeAsync parameter injection rather than being root-captured by the middleware constructor");
    }

    [Fact]
    public async Task Handler_writing_via_body_writer_without_flush_replays_full_body()
    {
        var payload = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");

        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/pipewriter", ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var mem = ctx.Response.BodyWriter.GetMemory(payload.Length);
                payload.CopyTo(mem);
                ctx.Response.BodyWriter.Advance(payload.Length);

                // Intentionally do NOT call FlushAsync on BodyWriter: the captured snapshot
                // would be empty unless the middleware flushes the cached PipeWriter before
                // reading the capture buffer.
                return Task.CompletedTask;
            }).WithMetadata(new IdempotentAttribute()));

        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/pipewriter", first, TestContext.Current.CancellationToken);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        firstBody.Should().Be("{\"hello\":\"world\"}");

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/pipewriter", second, TestContext.Current.CancellationToken);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResp.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
        var secondBody = await secondResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        secondBody.Should().Be(
            "{\"hello\":\"world\"}",
            "responses written via Response.BodyWriter.GetMemory + Advance without explicit FlushAsync must still be captured into the snapshot for replay");
    }

    [Fact]
    public async Task Concurrent_same_key_in_flight_returns_409_with_Retry_After()
    {
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = await BuildHost(configureEndpoints: endpoints =>
            endpoints.MapPost("/slow", async ctx =>
            {
                // Signal AFTER reservation succeeds (handler only runs when TryReserveAsync
                // returned Reserved) so the second request below can race the first
                // deterministically rather than via Task.Delay.
                handlerEntered.TrySetResult();
                await gate.Task.WaitAsync(ctx.RequestAborted);
                ctx.Response.StatusCode = 201;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"created\":true}", ctx.RequestAborted);
            }).WithMetadata(new IdempotentAttribute()));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();
        Task<HttpResponseMessage>? firstTask = null;

        try
        {
            var first = JsonBody("{}");
            first.Headers.Add(KeyHeader, key);
            firstTask = client.PostAsync("/slow", first, TestContext.Current.CancellationToken);

            // Wait until the first handler is in the gate; this proves TryReserveAsync
            // returned Reserved before the second request fires.
            await handlerEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

            var second = JsonBody("{}");
            second.Headers.Add(KeyHeader, key);
            var secondResp = await client.PostAsync("/slow", second, TestContext.Current.CancellationToken);

            secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            secondResp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

            secondResp.Headers.Contains("Retry-After").Should().BeTrue(
                "the in-flight outcome must surface Retry-After so clients can back off rather than hot-loop");
            var retryAfterRaw = secondResp.Headers.GetValues("Retry-After").Single();
            var retryAfter = int.Parse(retryAfterRaw, System.Globalization.CultureInfo.InvariantCulture);
            retryAfter.Should().BeInRange(1, 30,
                "Retry-After must be at least one second (per the floor in WriteInFlightAsync) and at most the default ReservationTimeout");

            var body = await secondResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var problem = System.Text.Json.JsonDocument.Parse(body);
            problem.RootElement.GetProperty("status").GetInt32().Should().Be(409);
            problem.RootElement.GetProperty("code").GetString().Should().Be(
                "idempotency.in_flight",
                "the in-flight Problem Details document must carry idempotency.in_flight in the code field so clients can branch on it, alongside the status URI for 409 in the type field");
            problem.RootElement.GetProperty("type").GetString().Should().Be(
                ProblemEnvelope.ProblemTypeForStatus(409),
                "the in-flight document resolves type from the same helper as every other emitter");

            gate.SetResult();
            var firstResp = await firstTask;
            firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            // Unblock the slow handler so the host can shut down cleanly even when an
            // assertion above fails before the explicit SetResult.
            gate.TrySetResult();
            if (firstTask is not null)
            {
                try
                {
                    using var firstResult = await firstTask;
                }
                catch
                {
                    // Cleanup of the first request must never mask the original assertion failure.
                }
            }
        }
    }

    [Fact]
    public async Task Empty_quoted_idempotency_key_is_rejected_with_400()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.TryAddWithoutValidation(KeyHeader, "\"\"");
        var response = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_invalid",
            "an empty quoted RFC 8941 sf-string parses to a zero-length key; every request sending Idempotency-Key: \"\" would otherwise share the same (scope, empty) store slot and silently replay each other's responses");
    }

    [Fact]
    public async Task Store_is_not_resolved_for_requests_that_bypass_idempotency()
    {
        var storeResolveCount = 0;
        var resolverResolveCount = 0;
        var shared = new InMemoryIdempotencyStore(new IdempotencyOptions(), TimeProvider.System);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddTrellisIdempotency();
                    s.AddScoped<IIdempotencyStore>(_ =>
                    {
                        Interlocked.Increment(ref storeResolveCount);
                        return new DelegatingIdempotencyStore(shared);
                    });
                    s.AddScoped<IIdempotencyScopeResolver>(_ =>
                    {
                        Interlocked.Increment(ref resolverResolveCount);
                        return new DelegatingIdempotencyScopeResolver();
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseTrellisIdempotency();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/passthrough", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("ok", ctx.RequestAborted);
                        });

                        endpoints.MapGet("/idempotent-get", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("get-ok", ctx.RequestAborted);
                        }).WithMetadata(new IdempotentAttribute());
                    });
                }));

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        var client = host.GetTestClient();

        // Bypass 1: endpoint without [Idempotent] metadata.
        var passthroughResp = await client.PostAsync("/passthrough", JsonBody("{}"), TestContext.Current.CancellationToken);
        passthroughResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Bypass 2: [Idempotent] endpoint but the request method (GET) is not in the
        // configured Methods set (default: POST + PATCH). Send a real key with it to
        // prove the pre-checks short-circuit even when a parseable key is present.
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/idempotent-get");
        getReq.Headers.Add(KeyHeader, Guid.NewGuid().ToString());
        var getResp = await client.SendAsync(getReq, TestContext.Current.CancellationToken);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        storeResolveCount.Should().Be(0,
            "requests that the middleware short-circuits (no [Idempotent] metadata or non-mutating method) must not trigger IIdempotencyStore construction; eagerly resolving via InvokeAsync parameter injection forces every pass-through request to pay store construction cost (and to create a scoped EF-backed DbContext) for nothing");
        resolverResolveCount.Should().Be(0,
            "the same lazy-resolution requirement applies to IIdempotencyScopeResolver: a tenant-aware resolver may depend on its own scoped services (e.g. an EF lookup or external identity call) and must not be constructed for pass-through requests");
    }

    // ---------------------------------------------------------------------
    // Problem-detail messages must reference the configured HeaderName
    // ---------------------------------------------------------------------
    // When the application customizes IdempotencyOptions.HeaderName (e.g. to
    // "X-My-Idem-Key"), every diagnostic the middleware emits to the client
    // must reference that configured name. Hard-coded "Idempotency-Key" in any
    // 400/409/422 problem-detail body would point clients at a header that
    // does not exist on their wire contract.

    private const string CustomKeyHeader = "X-Custom-Idem-Key";

    private static void UseCustomHeader(IdempotencyOptions o) => o.HeaderName = CustomKeyHeader;

    private static void AssertProblemReferencesConfiguredHeader(string body)
    {
        body.Should().Contain(CustomKeyHeader,
            $"problem-detail messages must reference the configured HeaderName ({CustomKeyHeader}) so clients are told which header is at fault");
        body.Should().NotContain("Idempotency-Key",
            "the default header name must not leak into a problem-detail body when the application configured a custom HeaderName");
    }

    [Fact]
    public async Task Missing_header_problem_detail_uses_configured_HeaderName()
    {
        using var host = await BuildHost(configureOptions: UseCustomHeader);
        var client = host.GetTestClient();

        var resp = await client.PostAsync("/idempotent", JsonBody("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_required");
        AssertProblemReferencesConfiguredHeader(body);
    }

    [Fact]
    public async Task Duplicate_header_problem_detail_uses_configured_HeaderName()
    {
        using var host = await BuildHost(configureOptions: UseCustomHeader);
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.TryAddWithoutValidation(CustomKeyHeader, "key-one");
        content.Headers.TryAddWithoutValidation(CustomKeyHeader, "key-two");
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_duplicate");
        AssertProblemReferencesConfiguredHeader(body);
    }

    [Fact]
    public async Task Invalid_key_problem_detail_uses_configured_HeaderName()
    {
        using var host = await BuildHost(configureOptions: UseCustomHeader);
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.TryAddWithoutValidation(CustomKeyHeader, "bad key with space");
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_invalid");
        AssertProblemReferencesConfiguredHeader(body);
    }

    [Fact]
    public async Task Key_too_long_problem_detail_uses_configured_HeaderName()
    {
        using var host = await BuildHost(configureOptions: o =>
        {
            o.HeaderName = CustomKeyHeader;
            o.MaxKeyLength = 10;
        });
        var client = host.GetTestClient();

        var content = JsonBody("{}");
        content.Headers.Add(CustomKeyHeader, new string('a', 20));
        var resp = await client.PostAsync("/idempotent", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_too_long");
        AssertProblemReferencesConfiguredHeader(body);
    }

    [Fact]
    public async Task Mismatch_problem_detail_uses_configured_HeaderName()
    {
        using var host = await BuildHost(configureOptions: UseCustomHeader);
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{\"x\":1}");
        first.Headers.Add(CustomKeyHeader, key);
        await client.PostAsync("/idempotent", first, TestContext.Current.CancellationToken);

        var second = JsonBody("{\"x\":2}");
        second.Headers.Add(CustomKeyHeader, key);
        var resp = await client.PostAsync("/idempotent", second, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("idempotency.key_reused_with_different_body");
        AssertProblemReferencesConfiguredHeader(body);
    }

    [Fact]
    public async Task In_flight_problem_detail_uses_configured_HeaderName()
    {
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = await BuildHost(
            configureEndpoints: endpoints =>
                endpoints.MapPost("/slow-custom", async ctx =>
                {
                    handlerEntered.TrySetResult();
                    await gate.Task.WaitAsync(ctx.RequestAborted);
                    ctx.Response.StatusCode = 201;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"created\":true}", ctx.RequestAborted);
                }).WithMetadata(new IdempotentAttribute()),
            configureOptions: UseCustomHeader);
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();
        Task<HttpResponseMessage>? firstTask = null;

        try
        {
            var first = JsonBody("{}");
            first.Headers.Add(CustomKeyHeader, key);
            firstTask = client.PostAsync("/slow-custom", first, TestContext.Current.CancellationToken);

            await handlerEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

            var second = JsonBody("{}");
            second.Headers.Add(CustomKeyHeader, key);
            var secondResp = await client.PostAsync("/slow-custom", second, TestContext.Current.CancellationToken);

            secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await secondResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            body.Should().Contain("idempotency.in_flight");
            AssertProblemReferencesConfiguredHeader(body);

            gate.SetResult();
            await firstTask;
        }
        finally
        {
            gate.TrySetResult();
            if (firstTask is not null)
            {
                try { (await firstTask).Dispose(); }
                catch { /* ignored */ }
            }
        }
    }

    private static async Task AssertCompleteFailureAbandonsAndRetryReExecutes(
        Func<Exception> completeExceptionFactory,
        string expectedLogMessage)
    {
        var executions = 0;
        var store = new ThrowingCompleteIdempotencyStore(completeExceptionFactory);
        using var loggerProvider = new CapturingLoggerProvider();
        using var host = await BuildHost(
            configureEndpoints: endpoints =>
                endpoints.MapPost("/complete-failure", async ctx =>
                {
                    Interlocked.Increment(ref executions);
                    ctx.Response.StatusCode = 201;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"created\":true}", ctx.RequestAborted);
                }).WithMetadata(new IdempotentAttribute()),
            configureLogging: logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(loggerProvider);
                logging.SetMinimumLevel(LogLevel.Trace);
            },
            configureServices: services => services.AddSingleton<IIdempotencyStore>(store));
        var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();

        var first = JsonBody("{}");
        first.Headers.Add(KeyHeader, key);
        var firstResp = await client.PostAsync("/complete-failure", first, TestContext.Current.CancellationToken);

        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);
        store.CompleteCallCount.Should().Be(1);
        store.AbandonCallCount.Should().Be(1,
            "a CompleteAsync failure must promptly release the in-flight reservation instead of waiting for ReservationTimeout");
        loggerProvider.Messages.Should().Contain(message => message.Contains(expectedLogMessage, StringComparison.Ordinal),
            "the original CompleteAsync failure cause must be logged before the best-effort abandon attempt");

        var second = JsonBody("{}");
        second.Headers.Add(KeyHeader, key);
        var secondResp = await client.PostAsync("/complete-failure", second, TestContext.Current.CancellationToken);

        secondResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "the immediate retry must be able to re-reserve the abandoned key instead of receiving idempotency.in_flight");
        secondResp.Headers.Contains("Idempotent-Replayed").Should().BeFalse(
            "CompleteAsync failed, so there is no persisted snapshot to replay");
        executions.Should().Be(2, "the retry must execute the handler after the first reservation is abandoned");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly Lock _lock = new();
        private readonly List<string> _messages = [];

        public string[] Messages
        {
            get
            {
                lock (_lock)
                    return [.. _messages];
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Capture(string message)
        {
            lock (_lock)
                _messages.Add(message);
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                provider.Capture(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class ThrowingCompleteIdempotencyStore : IIdempotencyStore
    {
        private readonly InMemoryIdempotencyStore _inner = new(new IdempotencyOptions(), TimeProvider.System);
        private readonly Func<Exception> _completeExceptionFactory;
        private int _completeCallCount;
        private int _abandonCallCount;

        public ThrowingCompleteIdempotencyStore(Func<Exception> completeExceptionFactory)
        {
            ArgumentNullException.ThrowIfNull(completeExceptionFactory);
            _completeExceptionFactory = completeExceptionFactory;
        }

        public int CompleteCallCount => Volatile.Read(ref _completeCallCount);

        public int AbandonCallCount => Volatile.Read(ref _abandonCallCount);

        public ValueTask<IdempotencyReservationOutcome> TryReserveAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken) =>
            _inner.TryReserveAsync(scope, key, fingerprint, cancellationToken);

        public ValueTask CompleteAsync(string scope, string key, string reservationId, IdempotencyResponseSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _completeCallCount);
            throw _completeExceptionFactory();
        }

        public ValueTask AbandonAsync(string scope, string key, string reservationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _abandonCallCount);
            return _inner.AbandonAsync(scope, key, reservationId, cancellationToken);
        }
    }

    private sealed class DelegatingIdempotencyScopeResolver : IIdempotencyScopeResolver
    {
        public ValueTask<string> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult("test-scope");
    }

    private sealed class DelegatingIdempotencyStore : IIdempotencyStore
    {
        private readonly IIdempotencyStore _inner;

        public DelegatingIdempotencyStore(IIdempotencyStore inner) => _inner = inner;

        public ValueTask<IdempotencyReservationOutcome> TryReserveAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken) =>
            _inner.TryReserveAsync(scope, key, fingerprint, cancellationToken);

        public ValueTask CompleteAsync(string scope, string key, string reservationId, IdempotencyResponseSnapshot snapshot, CancellationToken cancellationToken) =>
            _inner.CompleteAsync(scope, key, reservationId, snapshot, cancellationToken);

        public ValueTask AbandonAsync(string scope, string key, string reservationId, CancellationToken cancellationToken) =>
            _inner.AbandonAsync(scope, key, reservationId, cancellationToken);
    }
}