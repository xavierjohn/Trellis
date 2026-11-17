namespace Trellis.Asp;

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

internal sealed class PaginationOpenApiOptionsSetup : IConfigureNamedOptions<OpenApiOptions>
{
    public void Configure(OpenApiOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, OpenApiOptions options) =>
        options.AddOperationTransformer(TransformAsync);

    private static Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var pagination = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<PaginationAttribute>().LastOrDefault();
        if (pagination is null)
            return Task.CompletedTask;

        var parameters = operation.Parameters ??= [];
        AddIfMissing(parameters, new OpenApiParameter
        {
            Name = pagination.CursorParameter,
            In = ParameterLocation.Query,
            Required = false,
            Description = "Opaque continuation token. Omit for the first page; empty, whitespace-only, and repeated values are invalid.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1
            }
        });
        AddIfMissing(parameters, CreateLimitParameter(pagination));
        return Task.CompletedTask;
    }

    private static OpenApiParameter CreateLimitParameter(PaginationAttribute pagination)
    {
        var appliedDefault = Math.Min(pagination.DefaultSize, pagination.Max);
        return new OpenApiParameter
        {
            Name = pagination.LimitParameter,
            In = ParameterLocation.Query,
            Required = false,
            Description = string.Create(CultureInfo.InvariantCulture,
                $"Positive page size. Omission requests {pagination.DefaultSize} and applies {appliedDefault}. Explicit values above {pagination.Max} are {(pagination.Policy == PageSizeLimitPolicy.Clamp ? "clamped" : "rejected")}. Specify at most once."),
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = (pagination.Policy == PageSizeLimitPolicy.Reject ? pagination.Max : int.MaxValue)
                    .ToString(CultureInfo.InvariantCulture),
                Default = JsonValue.Create(appliedDefault)
            }
        };
    }

    private static void AddIfMissing(IList<IOpenApiParameter> parameters, OpenApiParameter parameter)
    {
        if (!parameters.Any(existing =>
            existing.In == ParameterLocation.Query
            && string.Equals(existing.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            parameters.Add(parameter);
    }
}
