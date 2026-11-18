namespace Trellis.Asp.OpenApi;

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Documents cursor and limit query parameters declared by <see cref="PaginationAttribute"/>.
/// </summary>
/// <remarks>
/// Register with <c>options.AddOperationTransformer&lt;TrellisPaginationOperationTransformer&gt;()</c>
/// inside the host's <c>AddOpenApi</c> callback for each participating document.
/// This transformer does not bind or parse request values, change input-origin metadata,
/// or overwrite existing query parameters.
/// </remarks>
public sealed class TrellisPaginationOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var pagination = context.Description.ActionDescriptor?.EndpointMetadata?
            .OfType<PaginationAttribute>().LastOrDefault();
        if (pagination is null)
            return Task.CompletedTask;

        var parameters = MutableParameters(operation);
        AddIfMissing(parameters, new OpenApiParameter
        {
            Name = pagination.CursorParameter,
            In = ParameterLocation.Query,
            Required = false,
            Description = "Opaque continuation token. Omit for the first page; empty, whitespace-only, and repeated values are invalid.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
                // Match .NET whitespace rules, not ECMAScript's different \s character set.
                Pattern = @"[^\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]"
            }
        });
        AddIfMissing(parameters, CreateLimitParameter(pagination));
        return Task.CompletedTask;
    }

    // An earlier transformer may have assigned an array or other fixed-size list.
    private static IList<IOpenApiParameter> MutableParameters(OpenApiOperation operation) =>
        operation.Parameters is { IsReadOnly: false } mutable
            ? mutable
            : operation.Parameters = [.. operation.Parameters ?? []];

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
                Default = SchemaDefault(pagination)
            }
        };
    }

    // Sending the default explicitly must equal omitting it: Clamp accepts any positive value, while
    // Reject would turn a default above the cap into a 422, so there is no honest value to publish.
    private static JsonValue? SchemaDefault(PaginationAttribute pagination) =>
        pagination.Policy == PageSizeLimitPolicy.Clamp || pagination.DefaultSize <= pagination.Max
            ? JsonValue.Create(pagination.DefaultSize)
            : null;

    private static void AddIfMissing(IList<IOpenApiParameter> parameters, OpenApiParameter parameter)
    {
        if (!parameters.Any(existing =>
            existing.In == ParameterLocation.Query
            && string.Equals(existing.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            parameters.Add(parameter);
    }
}
