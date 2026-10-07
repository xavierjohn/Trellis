namespace Trellis.Asp.Tests;

using System.Net;
using System.Text.Json;
using global::Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Mediator;
using Trellis.Testing;

public sealed class ResourceAuthorizationExposureHttpTests
{
    [Theory]
    [InlineData(ExposurePath.Direct, false, false, false)]
    [InlineData(ExposurePath.Direct, true, false, false)]
    [InlineData(ExposurePath.Via, false, false, false)]
    [InlineData(ExposurePath.Via, true, false, false)]
    [InlineData(ExposurePath.Projection, false, false, false)]
    [InlineData(ExposurePath.Projection, true, false, false)]
    [InlineData(ExposurePath.Direct, false, true, false)]
    [InlineData(ExposurePath.Direct, true, true, false)]
    [InlineData(ExposurePath.Via, false, true, false)]
    [InlineData(ExposurePath.Via, true, true, false)]
    [InlineData(ExposurePath.Projection, false, true, false)]
    [InlineData(ExposurePath.Projection, true, true, false)]
    [InlineData(ExposurePath.Direct, false, false, true)]
    [InlineData(ExposurePath.Direct, true, false, true)]
    [InlineData(ExposurePath.Via, false, false, true)]
    [InlineData(ExposurePath.Via, true, false, true)]
    [InlineData(ExposurePath.Projection, false, false, true)]
    [InlineData(ExposurePath.Projection, true, false, true)]
    [InlineData(ExposurePath.Direct, false, true, true)]
    [InlineData(ExposurePath.Direct, true, true, true)]
    [InlineData(ExposurePath.Via, false, true, true)]
    [InlineData(ExposurePath.Via, true, true, true)]
    [InlineData(ExposurePath.Projection, false, true, true)]
    [InlineData(ExposurePath.Projection, true, true, true)]
    public async Task Get_HideExistence_AbsentAndDenied_HaveIdenticalPublicResponses(
        ExposurePath path, bool mvc, bool publicMetadata, bool gone)
    {
        using var missingHost = CreateHost(missing: true, mvc, publicMetadata, gone);
        using var deniedHost = CreateHost(missing: false, mvc, publicMetadata, gone: false);
        using var missingClient = missingHost.GetTestClient();
        using var deniedClient = deniedHost.GetTestClient();
        var url = $"/hidden-resource/{path}/public-id";

        using var missingResponse = await missingClient.GetAsync(url, TestContext.Current.CancellationToken);
        using var deniedResponse = await deniedClient.GetAsync(url, TestContext.Current.CancellationToken);

        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        deniedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        missingResponse.Headers.CacheControl?.NoStore.Should().BeTrue();
        deniedResponse.Headers.CacheControl?.NoStore.Should().BeTrue();
        PublicHeaders(missingResponse).Should().BeEquivalentTo(PublicHeaders(deniedResponse));

        using var missingBody = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var deniedBody = JsonDocument.Parse(
            await deniedResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        PublicMetadata(missingBody).Should().BeEquivalentTo(PublicMetadata(deniedBody));
        missingBody.RootElement.GetProperty("code").GetString().Should().Be(
            publicMetadata ? "document.not-found" : "error.unspecified");
        if (publicMetadata)
            missingBody.RootElement.GetProperty("detail").GetString().Should().Be("Document not found.");
        missingBody.RootElement.GetProperty("kind").GetString().Should().Be("not-found");
        missingBody.RootElement.GetProperty("instance").GetString().Should().Be(url);
        missingHost.Services.GetRequiredService<ExposureState>().HandlerCalls.Should().Be(0);
        deniedHost.Services.GetRequiredService<ExposureState>().HandlerCalls.Should().Be(0);
    }

    [Fact]
    public void PublicHeaders_ResponseAndContentHeaders_CapturesAllStableFields()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        response.Headers.Vary.Add("Accept-Encoding");
        response.Headers.Add("X-Resource-State", "hidden");
        response.Content.Headers.Add("X-Payload-Version", "1");
        response.Headers.Add("X-Shared", "response");
        response.Content.Headers.Add("X-Shared", "content");
        response.Headers.Date = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);
        response.Headers.Add("TraceId", "request-trace");

        var headers = PublicHeaders(response);

        headers["vary"].Should().Equal(["Accept-Encoding"]);
        headers["x-resource-state"].Should().Equal(["hidden"]);
        headers["content-type"].Should().Equal(["text/plain; charset=utf-8"]);
        headers["x-payload-version"].Should().Equal(["1"]);
        headers["x-shared"].Should().Equal(["response", "content"]);
        headers.Should().NotContainKey("date");
        headers.Should().NotContainKey("traceid");
    }

    private static Dictionary<string, string[]> PublicHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .Where(header => !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase)
                && !header.Key.Equals("traceId", StringComparison.OrdinalIgnoreCase))
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key.ToLowerInvariant(),
                group => group.SelectMany(header => header.Value).ToArray());

    private static Dictionary<string, string> PublicMetadata(JsonDocument body) =>
        body.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .ToDictionary(property => property.Name, property => property.Value.GetRawText());

    private static IHost CreateHost(bool missing, bool mvc, bool publicMetadata, bool gone)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddTrellisAsp();
                    services.AddTrellisProblemDetails();
                    services.AddSingleton(new ExposureState(missing, gone));
                    services.AddScoped<ExposurePipeline>();
                    services.AddScoped<IActorProvider>(_ => new TestActorProvider("actor-1"));
                    services.AddScoped<IResourceLoader<DirectExposureQuery, ExposureDocument>, DocumentLoader>();
                    services.AddScoped<IResourceLoader<ViaExposureQuery, ExposureDocument>, ViaDocumentLoader>();
                    services.AddScoped<IResourceLoader<ProjectionExposureQuery, ExposureProjection>, ProjectionLoader>();
                    services.AddScoped<SharedResourceLoaderById<ExposureFolder, string>, FolderLoader>();
                    services.AddResourceAuthorization<DirectExposureQuery, ExposureDocument, Result<string>>();
                    services.AddRelatedResourceAuthorization<
                        ViaExposureQuery, ExposureDocument, string, ExposureFolder, string, Result<string>>(
                            document => document.FolderId);
                    services.AddResourceAuthorization<ProjectionExposureQuery, ExposureProjection, Result<string>>();
                    services.AddResourceAuthorization(options =>
                    {
                        if (publicMetadata)
                            options.HideExistence<ExposureDocument>("document.not-found", "Document not found.")
                                .HideExistence<ExposureProjection, ExposureDocument>("document.not-found", "Document not found.");
                        else
                            options.HideExistence<ExposureDocument>()
                                .HideExistence<ExposureProjection, ExposureDocument>();
                    });
                    if (mvc)
                        services.AddControllers().AddApplicationPart(typeof(ExposureController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        if (mvc)
                            endpoints.MapControllers();
                        else
                            endpoints.MapGet("/hidden-resource/{path}/{id}",
                                async (ExposurePath path, string id, ExposurePipeline pipeline, CancellationToken cancellationToken) =>
                                {
                                    var result = await pipeline.SendAsync(path, id, cancellationToken);
                                    return result.ToHttpResponse(options => options.WithCacheControl(CacheControl.NoStore()));
                                });
                    });
                }));
        return builder.Start();
    }

    public enum ExposurePath { Direct, Via, Projection }

    internal sealed class ExposureState(bool missing, bool gone)
    {
        internal bool Missing { get; } = missing;
        internal bool Gone { get; } = gone;
        internal int HandlerCalls { get; set; }
    }

    internal sealed record ExposureDocument(string OwnerId, string FolderId);
    internal sealed record ExposureProjection(string OwnerId);
    internal sealed record ExposureFolder(string OwnerId);

    internal sealed record DirectExposureQuery(string Id)
        : IQuery<Result<string>>, IAuthorizeResource<ExposureDocument>, IIdentifyResource<ExposureDocument, string>
    {
        public string GetResourceId() => Id;
        public Trellis.IResult Authorize(Actor actor, ExposureDocument resource) =>
            Result.Ensure(actor.IsOwner(resource.OwnerId), new Error.Forbidden("document.denied"));
    }

    internal sealed record ViaExposureQuery(string Id)
        : IQuery<Result<string>>, IAuthorizeResourceVia<ExposureFolder>, IIdentifyResource<ExposureDocument, string>
    {
        public string GetResourceId() => Id;
        public Trellis.IResult Authorize(Actor actor, IReadOnlyList<ExposureFolder> owners) =>
            Result.Ensure(owners.Any(owner => actor.IsOwner(owner.OwnerId)), new Error.Forbidden("folder.denied"));
    }

    internal sealed record ProjectionExposureQuery(string Id)
        : IQuery<Result<string>>, IAuthorizeResource<ExposureProjection>, IIdentifyResource<ExposureDocument, string>
    {
        public string GetResourceId() => Id;
        public Trellis.IResult Authorize(Actor actor, ExposureProjection resource) =>
            Result.Ensure(actor.IsOwner(resource.OwnerId), new Error.Forbidden("projection.denied"));
    }

    private static Result<T> Load<T>(ExposureState state, T resource)
    {
        if (!state.Missing)
            return Result.Ok(resource);

        var resourceRef = ResourceRef.For("InternalStorage", "database-id");
        Error error = state.Gone ? new Error.Gone(resourceRef) : new Error.NotFound(resourceRef);
        return Result.Fail<T>(error with
        {
            Code = state.Gone ? "storage.row-gone" : "storage.row-missing",
            Detail = state.Gone
                ? "Internal database-id was purged from tenant secret-tenant."
                : "Internal database-id was not found in tenant secret-tenant.",
            Cause = new Error.Forbidden("storage.partition-hidden"),
        });
    }

    private sealed class DocumentLoader(ExposureState state) : IResourceLoader<DirectExposureQuery, ExposureDocument>
    {
        public Task<Result<ExposureDocument>> LoadAsync(DirectExposureQuery message, CancellationToken cancellationToken) =>
            Task.FromResult(Load(state, new ExposureDocument("owner-1", "folder-1")));
    }

    private sealed class ViaDocumentLoader(ExposureState state) : IResourceLoader<ViaExposureQuery, ExposureDocument>
    {
        public Task<Result<ExposureDocument>> LoadAsync(ViaExposureQuery message, CancellationToken cancellationToken) =>
            Task.FromResult(Load(state, new ExposureDocument("owner-1", "folder-1")));
    }

    private sealed class ProjectionLoader(ExposureState state) : IResourceLoader<ProjectionExposureQuery, ExposureProjection>
    {
        public Task<Result<ExposureProjection>> LoadAsync(ProjectionExposureQuery message, CancellationToken cancellationToken) =>
            Task.FromResult(Load(state, new ExposureProjection("owner-1")));
    }

    private sealed class FolderLoader : SharedResourceLoaderById<ExposureFolder, string>
    {
        public override Task<Result<ExposureFolder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new ExposureFolder("owner-1")));
    }

    internal sealed class ExposurePipeline(IServiceProvider serviceProvider, ExposureState state)
    {
        internal ValueTask<Result<string>> SendAsync(ExposurePath path, string id, CancellationToken cancellationToken) =>
            path switch
            {
                ExposurePath.Direct => SendAsync(new DirectExposureQuery(id), cancellationToken),
                ExposurePath.Via => SendAsync(new ViaExposureQuery(id), cancellationToken),
                ExposurePath.Projection => SendAsync(new ProjectionExposureQuery(id), cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            };

        private ValueTask<Result<string>> SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
            where TMessage : IAuthorizationMessage, IMessage
        {
            MessageHandlerDelegate<TMessage, Result<string>> next = (_, _) =>
            {
                state.HandlerCalls++;
                return new ValueTask<Result<string>>(Result.Ok("authorized"));
            };
            foreach (var behavior in serviceProvider.GetServices<IPipelineBehavior<TMessage, Result<string>>>().Reverse())
            {
                var inner = next;
                next = (current, token) => behavior.Handle(current, inner, token);
            }

            return next(message, cancellationToken);
        }
    }
}

[ApiController]
[Route("hidden-resource")]
public sealed class ExposureController : ControllerBase
{
    [HttpGet("{path}/{id}")]
    public async Task<ActionResult<string>> Get(
        ResourceAuthorizationExposureHttpTests.ExposurePath path,
        string id,
        CancellationToken cancellationToken)
    {
        var pipeline = HttpContext.RequestServices
            .GetRequiredService<ResourceAuthorizationExposureHttpTests.ExposurePipeline>();
        var result = await pipeline.SendAsync(path, id, cancellationToken);
        return result.ToHttpResponse(options => options.WithCacheControl(CacheControl.NoStore())).AsActionResult<string>();
    }
}
