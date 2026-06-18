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

            public sealed record CreateCustomerRequest(ShippingAddress {|#0:ShippingAddress|});

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
                .WithLocation(0)
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

            public sealed record CustomerResponse(ShippingAddress {|#0:ShippingAddress|});

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
                .WithLocation(0)
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

            public sealed record CreateCustomerRequest(ShippingAddress {|#0:ShippingAddress|});

            public static class CustomerEndpoints
            {
                public static void Map(object app) =>
                    app.MapPost("/customers", (CreateCustomerRequest request) => { });
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(0)
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

            public sealed record CreateCustomerRequest(ShippingAddress {|#0:ShippingAddress|});

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
                .WithLocation(0)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task ServerBuiltMediatorCommand_WithOwnedValueObject_NoDiagnostic()
    {
        // A Mediator command constructed server-side (mapped from a separate request DTO, never
        // deserialized from the HTTP body) must NOT be flagged: System.Text.Json never touches it,
        // so there is no TryCreate-bypass risk and [JsonConverter] would be dead weight. TRLS020
        // only fires at real binding seams ([FromBody]/controller return, Minimal API handler params),
        // not on every Mediator message type.
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

            // Result<int> is a placeholder because the analyzer test stubs do not include Unit.
            public sealed record CreateCustomerCommand(ShippingAddress ShippingAddress) : ICommand<Result<int>>;
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(source);
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task MediatorCommandAsMinimalApiBody_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        // Coverage retained after dropping the blanket Mediator-message rule: a command bound DIRECTLY
        // as a Minimal API request body is still flagged via the endpoint-mapping seam.
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Mediator;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            namespace Mediator
            {
                public interface ICommand<TResponse> { }
            }

            namespace Microsoft.AspNetCore.Builder
            {
                public delegate void CreateCustomerCommandHandler(global::TestNamespace.CreateCustomerCommand command);

                public static class EndpointRouteBuilderExtensions
                {
                    public static void MapPost(this object builder, string pattern, CreateCustomerCommandHandler handler) { }
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

            // Result<int> is a placeholder because the analyzer test stubs do not include Unit.
            public sealed record CreateCustomerCommand(ShippingAddress {|#0:ShippingAddress|}) : ICommand<Result<int>>;

            public static class CustomerEndpoints
            {
                public static void Map(object app) =>
                    app.MapPost("/customers", (CreateCustomerCommand command) => { });
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(0)
                .WithArguments("ShippingAddress", "CreateCustomerCommand.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task MediatorCommandViaDelegateLocalMinimalApiBody_WithOwnedValueObjectMissingConverter_ReportsDiagnostic()
    {
        // Coverage retention for the delegate-valued handler shape: the command is bound as the
        // Minimal API body, but the handler is passed as a delegate local (not an inline lambda or
        // method group). The endpoint seam must still resolve the delegate's Invoke signature.
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Mediator;
            using Trellis;
            using Trellis.EntityFrameworkCore;

            namespace Mediator
            {
                public interface ICommand<TResponse> { }
            }

            namespace Microsoft.AspNetCore.Builder
            {
                public delegate void CreateCustomerCommandHandler(global::TestNamespace.CreateCustomerCommand command);

                public static class EndpointRouteBuilderExtensions
                {
                    public static void MapPost(this object builder, string pattern, CreateCustomerCommandHandler handler) { }
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

            // Result<int> is a placeholder because the analyzer test stubs do not include Unit.
            public sealed record CreateCustomerCommand(ShippingAddress {|#0:ShippingAddress|}) : ICommand<Result<int>>;

            public static class CustomerEndpoints
            {
                public static void Map(object app)
                {
                    CreateCustomerCommandHandler handler = (CreateCustomerCommand command) => { };
                    app.MapPost("/customers", handler);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CompositeValueObjectDtoConverterAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter)
                .WithLocation(0)
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

    // ---- Maybe<TComposite> coverage (TRLS020 extension) ----------------------
    //
    // The cookbook (trellis-api-cookbook.md:1097) calls Maybe<TComposite> on DTOs
    // a "correctness bug, not just an ergonomics one" — `MaybeScalarValueJsonConverterFactory`
    // refuses to convert when the inner T isn't `IScalarValue<,>`, so STJ falls back to
    // default construction of the composite, bypassing TryCreate validation.
    //
    // The bare composite case is already covered above. These tests extend the analyzer
    // to also flag Maybe<TComposite> on DTO surfaces.

    [Fact]
    public async Task FromBodyDto_WithMaybeOwnedValueObjectMissingConverter_ReportsDiagnostic()
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

            public sealed record CreateCustomerRequest(Maybe<ShippingAddress> {|#0:ShippingAddress|});

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
                .WithLocation(0)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task FromBodyDto_WithMaybeOwnedValueObjectEvenWithInnerConverter_StillReportsDiagnostic()
    {
        // Maybe<TComposite> on a DTO is ALWAYS broken regardless of whether the inner composite
        // carries [JsonConverter]. Trellis ships no MaybeCompositeValueObjectJsonConverterFactory;
        // the converter on TComposite doesn't cover Maybe<TComposite> deserialization. The
        // supported transport per cookbook Recipe 14 is `TComposite?` + Maybe.From(...) at the
        // controller seam.
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

            public sealed record CreateCustomerRequest(Maybe<ShippingAddress> {|#0:ShippingAddress|});

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
                .WithLocation(0)
                .WithArguments("ShippingAddress", "CreateCustomerRequest.ShippingAddress"));
        test.TestState.Sources.Add(("AspAndTrellisStubs.cs", AspAndTrellisStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task FromBodyDto_WithMaybePrimitiveInner_NoDiagnostic()
    {
        // Scope-boundary regression guard: TRLS020 specifically targets owned composite
        // value objects (`[OwnedEntity]` + `ValueObject`). Whether `Maybe<int>` / `Maybe<string>`
        // / `Maybe<Guid>` on DTOs is itself supported is a separate question — primitive
        // inner types are NOT handled by `MaybeScalarValueJsonConverterFactory` either (it
        // only converts `Maybe<TScalarValueObject>` where the inner type implements
        // `IScalarValue<,>`). This test only asserts that TRLS020 does not fire on
        // non-composite inner types, regardless of whether the broader pattern is sound.
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using Trellis;

            public sealed record CreateCustomerRequest(Maybe<int> Age, Maybe<string> Nickname);

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
}