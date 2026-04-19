namespace Trellis.AspSourceGenerator.Tests;

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trellis;

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

    /// <summary>
    /// Value objects inheriting from RequiredInt&lt;T&gt; must be discovered and produce a generated JSON converter.
    /// </summary>
    [Fact]
    public void RequiredInt_Derivative_Is_Discovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Quantity : RequiredInt<Quantity>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(s => s.Contains("QuantityJsonConverter"),
            "the generator should discover Quantity : RequiredInt<Quantity> and emit a converter");
    }

    /// <summary>
    /// RequiredEnum derivatives must serialize using the string Value.
    /// </summary>
    [Fact]
    public void RequiredEnum_Derivative_Writes_String_Value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class OrderState : RequiredEnum<OrderState>
            {
                public static readonly OrderState Draft = new();
                public static readonly OrderState Confirmed = new();
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(source =>
            source.Contains("OrderStateJsonConverter")
            && source.Contains("writer.WriteStringValue(value.Value);"),
            "RequiredEnum JSON converters must emit the string Value");
    }

    /// <summary>
    /// Value objects with the same simple name in different namespaces must not collide in generated converter names.
    /// </summary>
    [Fact]
    public void Same_Simple_Name_In_Different_Namespaces_Does_Not_Collide()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace Sales
            {
                public sealed class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
                {
                    private Quantity(int value) : base(value) { }

                    public static Result<Quantity> TryCreate(int value, string? fieldName = null) => new Quantity(value);
                    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
                        int.TryParse(value, out var v) ? new Quantity(v) : Result.Fail<Quantity>(Error.Validation("Invalid", fieldName));
                }
            }

            namespace Support
            {
                public sealed class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
                {
                    private Quantity(int value) : base(value) { }

                    public static Result<Quantity> TryCreate(int value, string? fieldName = null) => new Quantity(value);
                    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
                        int.TryParse(value, out var v) ? new Quantity(v) : Result.Fail<Quantity>(Error.Validation("Invalid", fieldName));
                }
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator should use unique converter names for scalar values that share a simple type name");
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

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the source generator should not produce errors");

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();
    }

    private static (List<string> Sources, IReadOnlyList<Diagnostic> Diagnostics, List<string> HintNames) RunGeneratorWithDiagnostics(
        string source, CancellationToken cancellationToken)
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
            out var outputCompilation,
            out var diagnostics,
            cancellationToken);

        var allDiagnostics = diagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToList();

        var generatedSources = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();

        var hintNames = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToList();

        return (generatedSources, allDiagnostics, hintNames);
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Concat([
                typeof(RequiredGuid<>).Assembly.Location,
                typeof(ScalarValueObject<,>).Assembly.Location,
                typeof(System.Text.Json.JsonSerializer).Assembly.Location,
                typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}
