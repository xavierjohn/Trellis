namespace Trellis.EntityFrameworkCore.Generator.Tests;

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Unit tests for <see cref="ApplyTrellisConventionsForGenerator"/>.
/// </summary>
/// <remarks>
/// These tests use compile-only stubs for <c>DbContext</c>, <c>DbSet&lt;T&gt;</c>,
/// <c>ScalarValueObject&lt;,&gt;</c>, <c>RequiredEnum&lt;&gt;</c>, <c>Maybe&lt;T&gt;</c>, and
/// <c>ValueObject</c>. The generator looks these up by metadata name so the stubs are sufficient
/// to drive discovery and emission. End-to-end behavior is covered by the integration tests in
/// <c>Trellis.EntityFrameworkCore.Tests.ApplyTrellisConventionsForTests</c>.
/// </remarks>
public class ApplyTrellisConventionsForGeneratorTests
{
    private const string Stubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public abstract class DbContext { }
            public class DbSet<T> { }
            public class ModelConfigurationBuilder { }
        }
        namespace Trellis
        {
            public abstract class ValueObject { }
            public abstract class ScalarValueObject<TSelf, T> : ValueObject { }
            public abstract class RequiredEnum<TSelf> : ValueObject where TSelf : RequiredEnum<TSelf> { }
            public readonly struct Maybe<T> { }
        }
        namespace Trellis.EntityFrameworkCore
        {
            public static class ModelConfigurationBuilderExtensions
            {
                public static Microsoft.EntityFrameworkCore.ModelConfigurationBuilder ApplyTrellisConventionsCore(
                    this Microsoft.EntityFrameworkCore.ModelConfigurationBuilder b,
                    System.Collections.Generic.IEnumerable<(System.Type ClrType, System.Type ProviderType)> scalars,
                    System.Collections.Generic.IEnumerable<System.Type> composites)
                    => b;
            }
        }
        """;

    [Fact]
    public void Empty_compilation_produces_no_output()
    {
        var ct = TestContext.Current.CancellationToken;

        var (sources, diagnostics, hints) = RunGenerator(Stubs, ct);

        sources.Should().BeEmpty();
        hints.Should().BeEmpty();
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Concrete_DbContext_with_scalar_VO_emits_registration()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class CustomerId : Trellis.ScalarValueObject<CustomerId, System.Guid> { }
                public class Customer
                {
                    public CustomerId Id { get; set; }
                }
                public class MyDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Customer> Customers { get; set; }
                }
            }
            """;

        var (sources, diagnostics, hints) = RunGenerator(src, ct);

        sources.Should().ContainSingle();
        hints.Should().ContainSingle().Which.Should().Be("GeneratedTrellisConventions.g.cs");
        var generated = sources[0];
        generated.Should().Contain("ApplyTrellisConventionsFor<TContext>");
        generated.Should().Contain("typeof(global::MyApp.MyDb)");
        generated.Should().Contain("typeof(global::MyApp.CustomerId)");
        generated.Should().Contain("typeof(global::System.Guid)");
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void RequiredEnum_property_emits_string_provider()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class Status : Trellis.RequiredEnum<Status> { }
                public class Order { public Status Status { get; set; } }
                public class MyDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
                }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        var generated = sources.Single();
        generated.Should().Contain("typeof(global::MyApp.Status)");
        generated.Should().Contain("typeof(global::System.String)");
    }

    [Fact]
    public void Maybe_wrapped_VO_property_is_unwrapped()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class Phone : Trellis.ScalarValueObject<Phone, string> { }
                public class Customer { public Trellis.Maybe<Phone> PhoneNumber { get; set; } }
                public class MyDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Customer> Customers { get; set; }
                }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        sources.Single().Should().Contain("typeof(global::MyApp.Phone)");
    }

    [Fact]
    public void Composite_VO_property_emits_composites_entry()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class Address : Trellis.ValueObject { }
                public class Customer { public Address Home { get; set; } }
                public class MyDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Customer> Customers { get; set; }
                }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        sources.Single().Should().Contain("typeof(global::MyApp.Address)");
    }

    [Fact]
    public void Abstract_DbContext_is_skipped()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public abstract class AbstractDb : Microsoft.EntityFrameworkCore.DbContext { }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        sources.Should().BeEmpty();
    }

    [Fact]
    public void Private_nested_DbContext_is_skipped()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class Holder
                {
                    private class HiddenDb : Microsoft.EntityFrameworkCore.DbContext { }
                }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        sources.Should().BeEmpty();
    }

    [Fact]
    public void Two_DbContexts_emit_two_dispatch_entries()
    {
        var ct = TestContext.Current.CancellationToken;

        var src = Stubs + """

            namespace MyApp
            {
                public class CustomerId : Trellis.ScalarValueObject<CustomerId, System.Guid> { }
                public class A { public CustomerId Id { get; set; } }
                public class B { public CustomerId Id { get; set; } }
                public class DbOne : Microsoft.EntityFrameworkCore.DbContext
                { public Microsoft.EntityFrameworkCore.DbSet<A> Items { get; set; } }
                public class DbTwo : Microsoft.EntityFrameworkCore.DbContext
                { public Microsoft.EntityFrameworkCore.DbSet<B> Items { get; set; } }
            }
            """;

        var (sources, _, _) = RunGenerator(src, ct);

        var generated = sources.Single();
        generated.Should().Contain("typeof(global::MyApp.DbOne)");
        generated.Should().Contain("typeof(global::MyApp.DbTwo)");
    }

    private static (List<string> Sources, IReadOnlyList<Diagnostic> Diagnostics, List<string> HintNames) RunGenerator(
        string source, System.Threading.CancellationToken ct)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "ApplyTrellisConventionsForGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ApplyTrellisConventionsForGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags, ct);

        var result = driver.GetRunResult();
        var sources = result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
        var hints = result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName).ToList();
        return (sources, diags, hints);
    }
}
