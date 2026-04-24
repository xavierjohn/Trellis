// Cookbook Recipe 4 — Minimal-API endpoint wiring Result<T> → ToHttpResponse.
namespace CookbookSnippets.Recipe04;

using System.Threading;
using global::Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe02;
using CookbookSnippets.Stubs;

public static class MinimalApiSample
{
    public static void Configure(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddTrellisAsp();
        builder.Services.AddOrdersFeature();

        var app = builder.Build();

        app.MapGet("/orders/{id:guid}", async (System.Guid id, IMediator mediator, CancellationToken ct) =>
        {
            Result<Order> result = await mediator.Send(new GetOrderQuery(id), ct);

            return result.ToHttpResponse(opts => opts
                .WithETag(o => o.ETag)
                .WithLastModified(o => o.LastModified)
                .Vary("Accept", "Accept-Language")
                .EvaluatePreconditions());
        });

        app.Run();
    }
}
