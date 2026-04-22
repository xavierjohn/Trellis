namespace Trellis.EntityFrameworkCore.Generator.Tests;

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Tests for <see cref="MaybePartialPropertyGenerator"/> to verify it correctly handles
/// init accessors on partial Maybe&lt;T&gt; properties.
/// </summary>
public class MaybePartialPropertyGeneratorTests
{
    /// <summary>
    /// Verifies that a Maybe&lt;T&gt; property declared with { get; init; } produces
    /// a generated implementation using an init accessor, not a set accessor.
    /// </summary>
    [Fact]
    public void Init_Accessor_Should_Produce_Init_In_Generated_Code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public partial Maybe<string> NickName { get; init; }
            }
            """;

        var (generatedSources, diagnostics, _) = RunGenerator(source, cancellationToken);

        // The generated code must compile — CS9250 would mean init/set mismatch
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial property implementation must match the init accessor");

        // Verify the generated code contains 'init =>' not 'set =>'
        generatedSources.Should().Contain(s => s.Contains("init =>"),
            "the generator should emit 'init' accessor when the property declaration uses 'init'");
    }

    /// <summary>
    /// Regression: { get; set; } should continue to work as before.
    /// </summary>
    [Fact]
    public void Set_Accessor_Should_Produce_Set_In_Generated_Code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public partial Maybe<string> NickName { get; set; }
            }
            """;

        var (generatedSources, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated code with set accessor should compile");

        generatedSources.Should().Contain(s => s.Contains("set =>"),
            "the generator should emit 'set' accessor when the property declaration uses 'set'");
    }

    /// <summary>
    /// Verifies nested containing types with the same simple name do not collide into one generated file.
    /// </summary>
    [Fact]
    public void Nested_SameNamed_Containing_Types_Should_Not_Collide()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Sales
            {
                public partial class Customer
                {
                    public partial Maybe<string> NickName { get; set; }
                }
            }

            public partial class Support
            {
                public partial class Customer
                {
                    public partial Maybe<string> AlternateName { get; set; }
                }
            }
            """;

        var (generatedSources, diagnostics, hintNames) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator should emit separate nested partial implementations for each containing type path");

        hintNames.Should().Contain("TestNamespace.Sales.Customer.Maybe.g.cs");
        hintNames.Should().Contain("TestNamespace.Support.Customer.Maybe.g.cs");
        hintNames.Should().OnlyHaveUniqueItems();
        generatedSources.Should().HaveCount(2);
    }

    #region TRLS035 — Non-partial Maybe<T> property diagnostic

    /// <summary>
    /// A non-partial Maybe&lt;T&gt; auto-property on a partial class should emit TRLS035.
    /// </summary>
    [Fact]
    public void NonPartial_MaybeProperty_On_PartialClass_Should_Emit_TRLS035()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public Maybe<string> Phone { get; set; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS035")
            .Should().ContainSingle()
            .Which.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Phone");
    }

    /// <summary>
    /// A partial Maybe&lt;T&gt; property should NOT emit TRLS035 (correct usage).
    /// </summary>
    [Fact]
    public void Partial_MaybeProperty_Should_Not_Emit_TRLS035()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public partial Maybe<string> Phone { get; set; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS035")
            .Should().BeEmpty("partial Maybe<T> is correct usage");
    }

    /// <summary>
    /// A non-partial Maybe&lt;T&gt; property on a NON-partial class should NOT emit TRLS035
    /// because the generator cannot emit a partial implementation for non-partial types.
    /// </summary>
    [Fact]
    public void NonPartial_MaybeProperty_On_NonPartialClass_Should_Not_Emit_TRLS035()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public class Customer
            {
                public int Id { get; set; }
                public Maybe<string> Phone { get; set; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS035")
            .Should().BeEmpty("class is not partial — diagnostic should not fire");
    }

    /// <summary>
    /// Multiple non-partial Maybe&lt;T&gt; properties should each emit their own TRLS035.
    /// </summary>
    [Fact]
    public void Multiple_NonPartial_MaybeProperties_Should_Emit_Multiple_TRLS035()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public Maybe<string> Phone { get; set; }
                public Maybe<string> Email { get; set; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        var gen100 = diagnostics.Where(d => d.Id == "TRLS035").ToList();
        gen100.Should().HaveCount(2);
        gen100.Should().Contain(d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Phone"));
        gen100.Should().Contain(d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Email"));
    }

    /// <summary>
    /// TRLS035 message should include the inner type name.
    /// </summary>
    [Fact]
    public void TRLS035_Message_Should_Include_InnerType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Customer
            {
                public int Id { get; set; }
                public Maybe<int> LoyaltyPoints { get; set; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS035")
            .Should().ContainSingle()
            .Which.GetMessage(CultureInfo.InvariantCulture).Should().Contain("int");
    }

    #endregion

    private static (List<string> Sources, IReadOnlyList<Diagnostic> Diagnostics, List<string> HintNames) RunGenerator(
        string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "MaybeGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MaybePartialPropertyGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics,
            cancellationToken);

        var allDiagnostics = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToList();

        var sources = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();

        var hintNames = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToList();

        return (sources, allDiagnostics, hintNames);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        // Ensure Trellis.Core is included (contains Maybe<T> in namespace Trellis)
        var trellisLocation = typeof(Maybe<>).Assembly.Location;
        if (!references.Any(r => r.Display?.Equals(trellisLocation, StringComparison.OrdinalIgnoreCase) == true))
            references.Add(MetadataReference.CreateFromFile(trellisLocation));

        return references.ToArray();
    }
}
