namespace Trellis.Asp.OpenApi.Tests;

using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

public sealed class TrellisPaginationOperationTransformerTests
{
    [Fact]
    public async Task TransformAsync_ReadOnlyParameters_CopiesThemAndPreservesExistingDefinitions()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            ApplicationServices = services,
            Description = new ApiDescription
            {
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [new PaginationAttribute()] }
            }
        };
        var existing = new OpenApiParameter { Name = "filter", In = ParameterLocation.Query };
        var operation = new OpenApiOperation { Parameters = new IOpenApiParameter[] { existing } };
        var transformer = new TrellisPaginationOperationTransformer();

        await transformer.TransformAsync(operation, context, TestContext.Current.CancellationToken);

        operation.Parameters.Should().NotBeNull();
        operation.Parameters!.Select(parameter => parameter.Name).Should().Equal("filter", "cursor", "limit");
        operation.Parameters[0].Should().BeSameAs(existing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransformAsync_ApiDescription_MissingMetadata_LeavesOperationUnchanged(
        bool missingActionDescriptor)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            ApplicationServices = services,
            Description = new ApiDescription
            {
                ActionDescriptor = missingActionDescriptor
                    ? null!
                    : new ActionDescriptor { EndpointMetadata = null! }
            }
        };
        var parameters = new List<IOpenApiParameter>
        {
            new OpenApiParameter { Name = "existing", In = ParameterLocation.Query }
        };
        var operation = new OpenApiOperation { Parameters = parameters };
        var transformer = new TrellisPaginationOperationTransformer();

        await transformer.TransformAsync(operation, context, TestContext.Current.CancellationToken);

        operation.Parameters.Should().BeSameAs(parameters);
        parameters.Should().ContainSingle().Which.Name.Should().Be("existing");
    }
}
