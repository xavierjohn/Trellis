namespace Trellis.AspSourceGenerator.Tests;

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Tests for <see cref="ScalarValueJsonConverterGenerator"/> to verify it discovers
/// value objects that inherit from RequiredGuid, RequiredString, and other base types.
/// </summary>
public class ScalarValueJsonConverterGeneratorTests
{
    /// <summary>
    /// Value objects inheriting from RequiredGuid&lt;T&gt; must be discovered by the syntax predicate
    /// and produce a generated JSON converter.
    /// </summary>
    [Fact]
    public void RequiredGuid_Derivative_Is_Discovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class OrderId : RequiredGuid<OrderId>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(s => s.Contains("OrderIdJsonConverter"),
            "the generator should discover OrderId : RequiredGuid<OrderId> and emit a converter");
    }

    /// <summary>
    /// Value objects inheriting from RequiredString&lt;T&gt; must be discovered by the syntax predicate
    /// and produce a generated JSON converter.
    /// </summary>
    [Fact]
    public void RequiredString_Derivative_Is_Discovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;
            using System.ComponentModel.DataAnnotations;

            namespace TestNamespace;

            [StringLength(100)]
            public partial class FirstName : RequiredString<FirstName>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(s => s.Contains("FirstNameJsonConverter"),
            "the generator should discover FirstName : RequiredString<FirstName> and emit a converter");
    }

    /// <summary>
    /// Value objects explicitly implementing IScalarValue should continue to work (regression guard).
    /// </summary>
    [Fact]
    public void IScalarValue_Implementation_Is_Discovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public sealed class Temperature : ScalarValueObject<Temperature, decimal>,
                IScalarValue<Temperature, decimal>
            {
                private Temperature(decimal value) : base(value) { }

                public static Result<Temperature> TryCreate(decimal value, string? fieldName = null) =>
                    value.ToResult()
                        .Ensure(v => v >= -273.15m, Error.Validation("Below absolute zero", fieldName ?? "temperature"))
                        .Map(v => new Temperature(v));
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(s => s.Contains("TemperatureJsonConverter"),
            "the generator should discover Temperature : IScalarValue<Temperature, decimal> and emit a converter");
    }

    private static List<string> RunGenerator(string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "ScalarValueGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ScalarValueJsonConverterGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var diagnostics,
            cancellationToken);

        // No generator errors expected
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the source generator should not produce errors");

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}
