namespace Trellis.Analyzers.Tests;

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="CreatedAtRouteMissingApiVersionCodeFixProvider"/> (TRLS023 code fix).
/// Verifies the rewrite produces compilable code by adding the
/// <c>Trellis.Asp.ApiVersioning</c> using directive when missing.
/// </summary>
public sealed class CreatedAtRouteMissingApiVersionCodeFixProviderTests
{
    private const string StubSource = """
        namespace Microsoft.AspNetCore.Routing
        {
            using System.Collections.Generic;
            public class RouteValueDictionary : Dictionary<string, object> { }
        }

        namespace Asp.Versioning
        {
            using System;
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
            public sealed class ApiVersionAttribute : Attribute
            {
                public ApiVersionAttribute(string version) { }
            }
        }

        namespace Trellis.Asp
        {
            using System;
            using Microsoft.AspNetCore.Routing;

            public sealed class HttpResponseOptionsBuilder<TDomain>
            {
                public HttpResponseOptionsBuilder<TDomain> CreatedAtRoute(string routeName, Func<TDomain, RouteValueDictionary> routeValues) => this;
                public HttpResponseOptionsBuilder<TDomain> CreatedAtAction(string actionName, Func<TDomain, RouteValueDictionary> routeValues, string? controllerName = null) => this;
                public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id") => this;
                public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, RouteValueDictionary> routeValues) => this;
            }
        }

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

    [Fact]
    public async Task Fix_renames_member_and_adds_using_directive()
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

        const string fixedSource = """
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
                        c => new RouteValueDictionary { ["id"] = c.Id }).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_chains_WithVersionedRoute_after_WithLocation()
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
                    {|#0:opts.WithLocation(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|};
                }
            }
            """;

        const string fixedSource = """
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
                        c => new RouteValueDictionary { ["id"] = c.Id }).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("WithLocation"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_chains_WithVersionedRoute_after_WithLocation_single_id_overload()
    {
        const string source = """
            using Asp.Versioning;
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

        const string fixedSource = """
            using Asp.Versioning;
            using Trellis.Asp;
            using Trellis.Asp.ApiVersioning;

            public sealed record Customer(int Id);

            [ApiVersion("2026-11-12")]
            public class CustomersController
            {
                public void DoIt(HttpResponseOptionsBuilder<Customer> opts)
                {
                    opts.WithLocation("Customers_GetById", c => (object)c.Id).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("WithLocation"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_does_not_duplicate_existing_using_directive()
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
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|};
                }
            }
            """;

        const string fixedSource = """
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
                        c => new RouteValueDictionary { ["id"] = c.Id }).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_does_not_duplicate_using_already_inside_file_scoped_namespace()
    {
        // Repo convention is file-scoped namespaces with usings *inside* the namespace block.
        // The previous HasUsing only checked CompilationUnitSyntax.Usings (top-level), so it
        // would re-add the using even when already in scope, producing a duplicate-using diagnostic.
        const string source = """
            namespace TestNs;

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
                    {|#0:opts.CreatedAtRoute(
                        "Customers_GetById",
                        c => new RouteValueDictionary { ["id"] = c.Id })|};
                }
            }
            """;

        const string fixedSource = """
            namespace TestNs;

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
                        c => new RouteValueDictionary { ["id"] = c.Id }).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_adds_using_inside_file_scoped_namespace_when_existing_usings_live_there()
    {
        // Repo convention: file-scoped namespace declaration with usings *inside* the namespace
        // block. The fix must add the new using to the same scope as the existing usings, not
        // above the namespace declaration.
        const string source = """
            namespace TestNs;

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

        const string fixedSource = """
            namespace TestNs;

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
                        c => new RouteValueDictionary { ["id"] = c.Id }).WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtRoute"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_chains_WithVersionedRoute_after_CreatedAtAction()
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

        const string fixedSource = """
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
                        "Customers").WithVersionedRoute();
                }
            }
            """;

        var test = new CSharpCodeFixTest<
            CreatedAtRouteMissingApiVersionAnalyzer,
            CreatedAtRouteMissingApiVersionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.FixedState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingApiVersionRouteValue)
                .WithLocation(0)
                .WithArguments("CreatedAtAction"));

        await test.RunAsync();
    }
}