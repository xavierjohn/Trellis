namespace Trellis.Analyzers.Tests;

using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for <see cref="CreatedAtRouteMissingApiVersionAnalyzer"/> (TRLS023).
/// Verifies that <c>HttpResponseOptionsBuilder&lt;T&gt;.CreatedAtRoute(...)</c> calls inside
/// <c>[ApiVersion]</c>-decorated controllers produce a warning when the route-values
/// dictionary literal does not include an <c>"api-version"</c> key.
/// </summary>
public sealed class CreatedAtRouteMissingApiVersionAnalyzerTests
{
    /// <summary>
    /// Stubs the minimal Trellis.Asp + Asp.Versioning + Microsoft.AspNetCore.Routing surface
    /// the analyzer needs to recognise.
    /// </summary>
    private const string StubSource = """
        namespace Microsoft.AspNetCore.Routing
        {
            using System.Collections.Generic;
            public partial class RouteValueDictionary : Dictionary<string, object> { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public abstract class HttpContext { }
        }

        namespace Asp.Versioning
        {
            using System;
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
            public sealed class ApiVersionAttribute : Attribute
            {
                public ApiVersionAttribute(string version) { }
            }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
            public sealed class ApiVersionNeutralAttribute : Attribute { }
        }

        namespace Trellis.Asp
        {
            using System;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public sealed class HttpResponseOptionsBuilder<TDomain>
            {
                public HttpResponseOptionsBuilder<TDomain> CreatedAtRoute(string routeName, Func<TDomain, RouteValueDictionary> routeValues) => this;
                public HttpResponseOptionsBuilder<TDomain> CreatedAtRoute(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id") => this;
                public HttpResponseOptionsBuilder<TDomain> CreatedAtAction(string actionName, Func<TDomain, RouteValueDictionary> routeValues, string? controllerName = null) => this;
                public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, RouteValueDictionary> routeValues) => this;
                public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id") => this;
                public HttpResponseOptionsBuilder<TDomain> WithRouteValueResolver(string key, Func<HttpContext, string?> resolver) => this;
            }
        }
        """;

    [Fact]
    public async Task CreatedAtRoute_chained_with_Trellis_WithVersionedRoute_produces_no_warning()
    {
        // The fluent chain CreatedAtRoute(...).WithVersionedRoute() means the api-version is
        // injected by the framework per-request — the literal dictionary doesn't need the key.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;
            using Trellis.Asp.ApiVersioning;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })
                        .WithVersionedRoute();
                }
            }
            """;

        const string apiVersioningStub = """
            namespace Trellis.Asp.ApiVersioning
            {
                using Trellis.Asp;

                public static class HttpResponseOptionsBuilderApiVersioningExtensions
                {
                    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
                        this HttpResponseOptionsBuilder<TDomain> builder) => builder;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.TestState.Sources.Add(("ApiVersioningStub.cs", apiVersioningStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_chained_with_foreign_WithVersionedRoute_extension_still_produces_warning()
    {
        // A user-defined .WithVersionedRoute(...) extension on the same builder type that lives
        // OUTSIDE Trellis.Asp.ApiVersioning must not falsely suppress TRLS023. The analyzer's
        // chain check resolves the symbol and verifies the containing type/namespace.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;
            using ContosoExtensions;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|}
                        .WithVersionedRoute();
                }
            }
            """;

        // Foreign extension method living in a different namespace with the same name.
        const string foreignStub = """
            namespace ContosoExtensions
            {
                using Trellis.Asp;

                public static class ContosoBuilderExtensions
                {
                    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
                        this HttpResponseOptionsBuilder<TDomain> builder) => builder;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.TestState.Sources.Add(("ForeignExtension.cs", foreignStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_versioned_controller_without_api_version_key_produces_warning()
    {
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_versioned_controller_with_api_version_key_produces_no_warning()
    {
        // The author already remembered to add api-version manually — no migration required.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id, ["api-version"] = "2026-11-12" });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_non_versioned_controller_produces_no_warning()
    {
        // No [ApiVersion] on the class — this controller doesn't participate in versioning at all,
        // so the api-version requirement doesn't apply.
        const string source = """
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_ApiVersionNeutral_controller_produces_no_warning()
    {
        // [ApiVersionNeutral] explicitly opts out of versioning — Location must NOT carry api-version.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersionNeutral]
            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_with_non_literal_routeValues_produces_no_warning()
    {
        // The analyzer can't inspect dynamically-built dictionaries — bail to false-negative
        // rather than false-positive. The runtime helper migration remains the right answer
        // but the warning would be unactionable on this shape.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    var rv = new RouteValueDictionary { ["id"] = 1 };
                    opts.CreatedAtRoute("Customers_GetById", _ => rv);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_with_case_insensitive_api_version_key_produces_no_warning()
    {
        // RouteValueDictionary keys are case-insensitive at runtime — "API-VERSION" already
        // satisfies the requirement, so the analyzer must not fire.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id, ["API-Version"] = "2026-11-12" });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_derived_controller_without_own_ApiVersion_produces_no_warning()
    {
        // [ApiVersion] is declared with Inherited = false. A derived class that doesn't carry
        // its own [ApiVersion] is NOT versioned by API Versioning — the analyzer must match.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class BaseController { }

            public class CustomersController : BaseController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_with_anonymous_object_ctor_arg_produces_warning()
    {
        // C# property names cannot contain hyphens, so `new RouteValueDictionary(new { id = 1 })`
        // can never carry an "api-version" key — the analyzer must fire.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary(new { id = c.Id }))|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        // The stubs declare RouteValueDictionary as inheriting from Dictionary<string,object>,
        // which doesn't have an (object) constructor — extend the stub locally just for this test.
        const string ctorStub = """
            namespace Microsoft.AspNetCore.Routing
            {
                public partial class RouteValueDictionary
                {
                    public RouteValueDictionary(object values) { }
                }
            }
            """;
        test.TestState.Sources.Add(("CtorStub.cs", ctorStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_with_const_string_api_version_key_produces_no_warning()
    {
        // `[ApiVersionKey] = ...` where `ApiVersionKey = "api-version"` is a const string —
        // the analyzer must resolve the constant value via the semantic model and treat it as
        // satisfying the api-version requirement.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public const string ApiVersionKey = "api-version";

                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id, [ApiVersionKey] = "2026-11-12" });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_on_method_level_ApiVersionNeutral_action_produces_no_warning()
    {
        // [ApiVersionNeutral] is valid on actions (AttributeTargets.Class | Method). A versioned
        // controller may contain a single neutral action — TRLS023 must respect method-level
        // neutrality and not fire on CreatedAtRoute calls inside that action.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                [ApiVersionNeutral]
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task WithLocation_on_versioned_controller_without_api_version_key_produces_warning()
    {
        // WithLocation (200 OK + Location) is subject to the same versioning trap as
        // CreatedAtRoute (201 + Location): under query/header API versioning the generated
        // Location must carry api-version or it 404s on dereference.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.WithLocation(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("WithLocation"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task WithLocation_chained_with_WithVersionedRoute_produces_no_warning()
    {
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;
            using Trellis.Asp.ApiVersioning;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.WithLocation(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })
                        .WithVersionedRoute();
                }
            }
            """;

        const string apiVersioningStub = """
            namespace Trellis.Asp.ApiVersioning
            {
                using Trellis.Asp;

                public static class HttpResponseOptionsBuilderApiVersioningExtensions
                {
                    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
                        this HttpResponseOptionsBuilder<TDomain> builder) => builder;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.TestState.Sources.Add(("ApiVersioningStub.cs", apiVersioningStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task WithLocation_single_id_overload_without_WithVersionedRoute_produces_warning()
    {
        // The single-id overload constructs a one-entry dictionary internally with the supplied
        // route key (default "id") and no api-version. Without .WithVersionedRoute() the
        // generated Location is missing the version.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.WithLocation("Customers_GetById", c => (object)c.Id)|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("WithLocation"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task Single_id_overload_with_method_group_selector_still_produces_warning()
    {
        // Regression: a method-group / delegate-variable selector on the 3-arg single-id
        // overload is not a LambdaExpressionSyntax, so the previous ClassifyRouteValuesShape
        // would classify it as Unrecognized and silently miss the diagnostic. The shape
        // classifier now routes non-lambda arguments to SingleIdSelector when the method
        // symbol identifies the single-id overload.
        const string source = """
            using System;
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                private static object GetId(Customer c) => c.Id;

                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtRoute("Customers_GetById", GetId)|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtAction_on_versioned_controller_without_api_version_key_produces_warning()
    {
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtAction(
                        "GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id },
                        "Customers")|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtAction"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtAction_chained_with_WithVersionedRoute_produces_no_warning()
    {
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;
            using Trellis.Asp.ApiVersioning;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtAction(
                        "GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id },
                        "Customers")
                        .WithVersionedRoute();
                }
            }
            """;

        const string apiVersioningStub = """
            namespace Trellis.Asp.ApiVersioning
            {
                using Trellis.Asp;

                public static class HttpResponseOptionsBuilderApiVersioningExtensions
                {
                    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
                        this HttpResponseOptionsBuilder<TDomain> builder) => builder;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.TestState.Sources.Add(("ApiVersioningStub.cs", apiVersioningStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtAction_with_non_literal_routeValues_produces_no_warning()
    {
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    var rv = new RouteValueDictionary { ["id"] = 1 };
                    opts.CreatedAtAction("GetById", _ => rv, "Customers");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_chained_with_WithRouteValueResolver_apiversion_literal_produces_no_warning()
    {
        // .WithRouteValueResolver("api-version", ctx => ...) is what .WithVersionedRoute()
        // does internally — equivalent suppression must be honored.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })
                        .WithRouteValueResolver("api-version", _ => "2026-11-12");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_chained_with_WithRouteValueResolver_apiversion_const_produces_no_warning()
    {
        // The first argument is a const-string identifier — the analyzer's constant folding
        // (TryGetConstantStringValue) must recognise it just like a string literal.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                private const string ApiVersionKey = "api-version";

                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })
                        .WithRouteValueResolver(ApiVersionKey, _ => "2026-11-12");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_chained_with_WithRouteValueResolver_API_VERSION_uppercase_produces_no_warning()
    {
        // RouteValueDictionary keys are case-insensitive at runtime — match the existing
        // analyzer's case-insensitive behavior for the "api-version" key.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })
                        .WithRouteValueResolver("API-Version", _ => "2026-11-12");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreatedAtRoute_chained_with_WithRouteValueResolver_unrelated_key_still_produces_warning()
    {
        // A WithRouteValueResolver for a different key (e.g. tenant-id) does NOT contribute
        // to api-version injection, so the warning must still fire.
        const string source = """
            using Asp.Versioning;
            using Microsoft.AspNetCore.Routing;
            using Trellis.Asp;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|}
                        .WithRouteValueResolver("tenant-id", _ => "acme");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CreatedAtRouteMissingApiVersionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));
        test.TestState.Sources.Add(("Stubs.cs", StubSource));

        await test.RunAsync();
    }
}