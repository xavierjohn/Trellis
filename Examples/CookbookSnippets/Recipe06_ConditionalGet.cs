// Cookbook Recipe 6 — Conditional GET with EntityTagValue and byte-range with RangeOutcome.
namespace CookbookSnippets.Recipe06;

using System.Threading;
using CookbookSnippets.Stubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp;

public static class ConditionalGetSample
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/blobs/{id:guid}", async (System.Guid id, HttpRequest req, IBlobRepository repo, CancellationToken ct) =>
        {
            Result<BlobContent> result = await repo.FindAsync(new BlobId(id), ct);

            return result.ToHttpResponse(opts => opts
                .WithETag(b => EntityTagValue.Strong(b.Sha256Hex))
                .WithLastModified(b => b.UploadedAt)
                .Vary("Range")
                .WithAcceptRanges("bytes")
                .WithRange(b =>
                {
                    var outcome = RangeRequestEvaluator.Evaluate(req, b.Length);
                    return outcome switch
                    {
                        RangeOutcome.PartialContent pc => new System.Net.Http.Headers.ContentRangeHeaderValue(pc.From, pc.To, pc.CompleteLength),
                        _ => new System.Net.Http.Headers.ContentRangeHeaderValue(b.Length),
                    };
                })
                .EvaluatePreconditions());
        });
}