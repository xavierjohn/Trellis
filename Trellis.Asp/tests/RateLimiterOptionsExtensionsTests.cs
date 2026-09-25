namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;

public sealed class RateLimiterOptionsExtensionsTests
{
    [Fact]
    public async Task UseTrellisRejectionHandler_RetryMetadataPresent_WritesCanonicalProblemDetails()
    {
        using var host = CreateHost(TimeSpan.FromMilliseconds(12_250));
        using var response = await host.GetTestClient()
            .GetAsync("/limited?scope=write", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.GetValues("Retry-After").Should().Equal("13");

        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        problem.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.RateLimitExceeded);
        problem.RootElement.GetProperty("kind").GetString().Should().Be("too-many-requests");
        problem.RootElement.GetProperty("instance").GetString().Should().Be("/limited?scope=write");
        problem.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_RetryMetadataAbsent_OmitsRetryAfter()
    {
        using var host = CreateHost();
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Contains("Retry-After").Should().BeFalse();

        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_ObserverConfigured_RunsBeforeTrellisWriter()
    {
        var observed = false;
        var responseStarted = true;
        var observedStatusCode = 0;

        using var host = CreateHost(
            TimeSpan.FromSeconds(5),
            (context, _) =>
            {
                observed = true;
                responseStarted = context.HttpContext.Response.HasStarted;
                observedStatusCode = context.HttpContext.Response.StatusCode;
                return ValueTask.CompletedTask;
            });
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        observed.Should().BeTrue();
        responseStarted.Should().BeFalse();
        observedStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("Retry-After").Should().Equal("5");
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_NamedPolicyWithoutHandler_UsesTrellisWriter()
    {
        using var host = CreateHost(namedPolicy: new RejectingPolicy(TimeSpan.FromSeconds(4), ownsResponse: false));
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("Retry-After").Should().Equal("4");

        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_NamedPolicyWithHandler_PolicyOwnsResponse()
    {
        using var host = CreateHost(namedPolicy: new RejectingPolicy(TimeSpan.FromSeconds(4), ownsResponse: true));
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status418ImATeapot);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/problem+json");
        response.Headers.Contains("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_InlinePolicyWithoutHandler_BypassesGlobalHandler()
    {
        using var host = CreateHost(
            namedPolicy: new RejectingPolicy(TimeSpan.FromSeconds(4), ownsResponse: false),
            inlinePolicy: true);
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/problem+json");
        response.Headers.Contains("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_ObserverMutatesResponse_Throws()
    {
        using var host = CreateHost(
            observer: (context, _) =>
            {
                context.HttpContext.Response.Headers["X-Observer"] = "not-allowed";
                return ValueTask.CompletedTask;
            });
        using var client = host.GetTestClient();

        Func<Task> act = () => client.GetAsync("/limited", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*observer must not mutate or start the HTTP response*");
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_ObserverMutatesResponseOnStarting_Throws()
    {
        using var host = CreateHost(
            observer: (context, _) =>
            {
                context.HttpContext.Response.OnStarting(() =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                    return Task.CompletedTask;
                });
                return ValueTask.CompletedTask;
            });
        using var client = host.GetTestClient();

        Func<Task> act = () => client.GetAsync("/limited", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*observer must not mutate or start the HTTP response*");
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_ObserverChangesHeaderOnStarting_Throws()
    {
        using var host = CreateHost(
            observer: (context, _) =>
            {
                context.HttpContext.Response.OnStarting(() =>
                {
                    context.HttpContext.Response.Headers["X-Observer"] = "not-allowed";
                    return Task.CompletedTask;
                });
                return ValueTask.CompletedTask;
            });
        using var client = host.GetTestClient();

        Func<Task> act = () => client.GetAsync("/limited", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*observer must not mutate or start the HTTP response*");
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_ObserverOnStartingWithoutMutation_PreservesResponse()
    {
        var observedStatus = 0;
        using var host = CreateHost(
            observer: (context, _) =>
            {
                context.HttpContext.Response.OnStarting(() =>
                {
                    observedStatus = context.HttpContext.Response.StatusCode;
                    return Task.CompletedTask;
                });
                return ValueTask.CompletedTask;
            });
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        observedStatus.Should().Be(StatusCodes.Status429TooManyRequests);
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task UseTrellisRejectionHandler_PreexistingOnStartingCallback_PreservesItsHeader()
    {
        using var host = CreateHost(
            observer: (_, _) => ValueTask.CompletedTask,
            addOuterOnStarting: true);
        using var response = await host.GetTestClient()
            .GetAsync("/limited", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("X-Outer").Should().Equal("kept");
    }

    [Fact]
    public void UseTrellisRejectionHandler_ExistingRejectionWriterConfigured_Throws()
    {
        var options = new RateLimiterOptions
        {
            OnRejected = (_, _) => ValueTask.CompletedTask,
        };

        Action act = () => options.UseTrellisRejectionHandler();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already has an OnRejected handler*");
    }

    [Fact]
    public void UseTrellisRejectionHandler_NullOptions_Throws()
    {
        Action act = () => ((RateLimiterOptions)null!).UseTrellisRejectionHandler();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseTrellisRejectionHandler_CustomRejectionStatus_ReplacesWith429()
    {
        var options = new RateLimiterOptions { RejectionStatusCode = StatusCodes.Status418ImATeapot };

        options.UseTrellisRejectionHandler();

        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    private static IHost CreateHost(
        TimeSpan? retryAfter = null,
        Func<OnRejectedContext, CancellationToken, ValueTask>? observer = null,
        IRateLimiterPolicy<string>? namedPolicy = null,
        bool inlinePolicy = false,
        bool addOuterOnStarting = false) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddTrellisAsp();
                    services.AddTrellisProblemDetails();
                    services.AddRateLimiter(options =>
                    {
                        if (namedPolicy is null)
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                                _ => RateLimitPartition.Get(
                                    "all-requests",
                                    _ => new RejectingRateLimiter(retryAfter)));
                        else if (!inlinePolicy)
                            options.AddPolicy("write", namedPolicy);

                        options.UseTrellisRejectionHandler(observer);
                    });
                })
                .Configure(app =>
                {
                    if (addOuterOnStarting)
                        app.Use((context, next) =>
                        {
                            context.Response.OnStarting(() =>
                            {
                                context.Response.Headers["X-Outer"] = "kept";
                                return Task.CompletedTask;
                            });
                            return next(context);
                        });

                    app.UseRouting();
                    app.UseRateLimiter();
                    if (namedPolicy is null)
                        app.Run(_ => throw new InvalidOperationException(
                            "The rate limiter should reject before the endpoint runs."));
                    else
                        app.UseEndpoints(endpoints =>
                        {
                            var endpoint = endpoints.MapGet("/limited",
                                (HttpContext _) => throw new InvalidOperationException(
                                    "The rate limiter should reject before the endpoint runs."));
                            if (inlinePolicy)
                                endpoint.RequireRateLimiting(namedPolicy);
                            else
                                endpoint.RequireRateLimiting("write");
                        });
                }))
            .Start();

    private sealed class RejectingPolicy(TimeSpan? retryAfter, bool ownsResponse) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected =>
            ownsResponse ? RejectAsync : null;

        public RateLimitPartition<string> GetPartition(HttpContext context) =>
            RateLimitPartition.Get("all-requests", _ => new RejectingRateLimiter(retryAfter));

        private static ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status418ImATeapot;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingRateLimiter(TimeSpan? retryAfter) : RateLimiter
    {
        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            new RejectedLease(retryAfter);

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<RateLimitLease>(new RejectedLease(retryAfter));
    }

    private sealed class RejectedLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames =>
            retryAfter.HasValue ? [MetadataName.RetryAfter.Name] : [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
