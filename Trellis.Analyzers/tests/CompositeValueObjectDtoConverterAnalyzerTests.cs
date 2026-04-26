namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for the composite value object DTO converter analyzer.
/// </summary>
public class CompositeValueObjectDtoConverterAnalyzerTests
{
    private const string AspAndTrellisStubSource = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class ApiControllerAttribute : System.Attribute { }
            public sealed class RouteAttribute : System.Attribute
            {
                public RouteAttribute(string template) { }
            }
            public sealed class HttpPostAttribute : System.Attribute { }
            public sealed class FromBodyAttribute : System.Attribute { }
            public sealed class ActionResult<T> { }
            public abstract class ControllerBase { }
        }

        namespace Trellis
        {
            using System.Collections.Generic;

            public abstract class ValueObject
            {
                protected abstract IEnumerable<System.IComparable?> GetEqualityComponents();
            }
        }

        namespace Trellis.EntityFrameworkCore
        {
            public sealed class OwnedEntityAttribute : System.Attribute { }
        }

        namespace Trellis.Primitives
        {
            public sealed class CompositeValueObjectJsonConverter<T> { }
        }
        """;

    [Fact]
    public async Task FromBodyDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerRequest(ShippingAddress ShippingAddress);

            [ApiController]
            [Route("customers")]
            public sealed class CustomersController : ControllerBase
            {
                [HttpPost]
                public void Create([FromBody] CreateCustomerRequest request) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(24, 60)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task ControllerResponseDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CustomerResponse(ShippingAddress ShippingAddress);

            [ApiController]
            [Route("customers")]
            public sealed class CustomersController : ControllerBase
            {
                public ActionResult<CustomerResponse> Get() => default!;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(24, 55)
                .WithArguments("ShippingAddress", "CustomerResponse.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task MinimalApiRequestDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            namespace Microsoft.AspNetCore.Builder
            {
                public delegate void CreateCustomerRequestHandler(global::TestNamespace.CreateCustomerRequest request);

                public static class EndpointRouteBuilderExtensions
                {
                    public static void MapPost(this object builder, string pattern, CreateCustomerRequestHandler handler) { }
                }
            }

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerRequest(ShippingAddress ShippingAddress);

            public static class CustomerEndpoints
            {
                public static void Map(object app) =>
                    app.MapPost("/customers", (CreateCustomerRequest request) => { });
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(34, 60)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task MinimalApiMethodGroupRequestDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            namespace Microsoft.AspNetCore.Builder
            {
                public delegate void CreateCustomerRequestHandler(global::TestNamespace.CreateCustomerRequest request);

                public static class EndpointRouteBuilderExtensions
                {
                    public static void MapPost(this object builder, string pattern, CreateCustomerRequestHandler handler) { }
                }
            }

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerRequest(ShippingAddress ShippingAddress);

            public static class CustomerEndpoints
            {
                public static void Map(object app) =>
                    app.MapPost("/customers", Create);

                private static void Create(CreateCustomerRequest request) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(34, 60)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task MediatorRequestDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        const string source = """
            using Mediator;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            namespace Mediator
            {
                public interface ICommand<TResponse> { }
            }

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerCommand(ShippingAddress ShippingAddress) : ICommand<Result>;
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(29, 60)
                .WithArguments("ShippingAddress", "CreateCustomerCommand.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task FromBodyDto_WithOwnedValueObjectAndConverter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using System.Text.Json.Serialization;
            using Trellis;
            using Trellis.EntityFrameworkCore;
            using Trellis.Primitives;

            [OwnedEntity]
            [JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerRequest(ShippingAddress ShippingAddress);

            [ApiController]
            [Route("customers")]
            public sealed class CustomersController : ControllerBase
            {
                [HttpPost]
                public void Create([FromBody] CreateCustomerRequest request) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(source);
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task UnrelatedMapPostMethod_WithOwnedValueObjectRequest_NoDiagnostic()
    {
        const string source = """
            using Trellis;
            using Trellis.EntityFrameworkCore;
            using MyCompany.Routing;

            namespace MyCompany.Routing
            {
                public static class RouteBuilderExtensions
                {
                    public static void MapPost<TDelegate>(this object builder, string pattern, TDelegate handler) { }
                }
            }

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed record CreateCustomerRequest(ShippingAddress ShippingAddress);

            public static class CustomerEndpoints
            {
                public static void Map(object app) =>
                    app.MapPost("/customers", (CreateCustomerRequest request) => { });
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(source);
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task PlainDomainType_NotUsedAsFromBodyDto_NoDiagnostic()
    {
        const string source = """
            using Trellis;
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class ShippingAddress : ValueObject
            {
                public string Street { get; }
                public string City { get; }

                protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                {
                    yield return Street;
                    yield return City;
                }
            }

            public sealed class Customer
            {
                public ShippingAddress ShippingAddress { get; set; } = default!;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(source);
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }
}
