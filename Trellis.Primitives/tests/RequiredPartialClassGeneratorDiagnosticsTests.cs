namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceGenerator;

/// <summary>
/// Tests for source-generator diagnostics emitted by <see cref="RequiredPartialClassGenerator"/>.
/// </summary>
public class RequiredPartialClassGeneratorDiagnosticsTests
{
    private static readonly Func<double, string?> s_formatDecimalLiteral =
        typeof(RequiredPartialClassGenerator)
            .GetMethod("FormatDecimalLiteral", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<double, string?>>();

    [Fact]
    public void InvalidStringLengthRange_Reports_TRLS032()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [StringLength(5, MinimumLength = 10)]
            public partial class ImpossibleName : RequiredString<ImpossibleName>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "TRLS032");

        var diagnostic = diagnostics.Single(d => d.Id == "TRLS032");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("ImpossibleName");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("StringLength(5, MinimumLength = 10)");
    }

    [Fact]
    public void SameClassNameInDifferentNamespaces_DoesNotCollideGeneratedHintNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace Sales
            {
                public partial class CustomerId : RequiredGuid<CustomerId>
                {
                }
            }

            namespace Support
            {
                public partial class CustomerId : RequiredGuid<CustomerId>
                {
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorNamespaceCollisionTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator should support identical type names in different namespaces without hint-name collisions");

        var generatedSources = driver.GetRunResult().Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.HintName)
            .ToArray();

        generatedSources.Should().HaveCount(2);
        generatedSources.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void NestedValueObjectInStaticClass_PreservesContainingTypeModifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public static partial class ValueObjects
            {
                public partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must match the static containing type");
    }

    [Fact]
    public void NestedValueObjectInSealedClass_PreservesContainingTypeModifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public sealed partial class Container
            {
                public partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must match the sealed containing type");
    }

    [Fact]
    public void NestedValueObjectWithProtectedInternalAccessibility_GeneratesValidAccessibility()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Container
            {
                protected internal partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must emit the C# keyword pair protected internal");
    }

    [Fact]
    public void NestedValueObjectWithPrivateProtectedAccessibility_GeneratesValidAccessibility()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Container
            {
                private protected partial class Code : RequiredString<Code>
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated partial declaration must emit the C# keyword pair private protected");
    }

    [Fact]
    public void GlobalNamespaceValueObject_GeneratesWithoutInvalidNamespaceDeclaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            public partial class GlobalCode : RequiredString<GlobalCode>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator must omit the namespace declaration for global-namespace value objects");
    }

    [Fact]
    public void CoreOnlyRequiredString_SourceGeneratorOutputDoesNotReferenceTrellisPrimitives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Code : RequiredString<Code>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken, GetCoreOnlyMetadataReferences());

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated source must not introduce a Trellis.Primitives reference");
    }

    [Fact]
    public void PositiveOnRequiredString_Reports_TRLS043()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Positive]
            public partial class SkuCode : RequiredString<SkuCode>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS043");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS043");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("SkuCode");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("[Positive]");
    }

    [Fact]
    public void TrimOnRequiredInt_Reports_TRLS057()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Trim]
            public partial class Quantity : RequiredInt<Quantity>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS057");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS057");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Quantity");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("[Trim]");
    }

    [Fact]
    public void NotDefaultOnRequiredBool_Reports_TRLS058()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [NotDefault]
            public partial class IsActive : RequiredBool<IsActive>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS058");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS058");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("IsActive");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("[NotDefault]");
    }

    [Fact]
    public void TrimAndNotDefaultOnRequiredString_ReportsNoPlacementDiagnostic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Trim, NotDefault]
            public partial class Name : RequiredString<Name>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().NotContain(d => d.Id == "TRLS057");
        diagnostics.Should().NotContain(d => d.Id == "TRLS058");
    }

    [Theory]
    [InlineData("[StringLength(5, Code = \"\")]", "RequiredString<Widget>", "[StringLength]")]
    [InlineData("[StringLength(5, Code = \"   \")]", "RequiredString<Widget>", "[StringLength]")]
    [InlineData("[Range(1, 5, Code = \"\")]", "RequiredInt<Widget>", "[Range]")]
    [InlineData("[NotDefault(Code = \"\")]", "RequiredGuid<Widget>", "[NotDefault]")]
    [InlineData("[Positive(Code = \"\")]", "RequiredInt<Widget>", "[Range]")]
    public void EmptyReasonCodeOverride_Reports_TRLS060(string attribute, string baseType, string attributeInMessage)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var source = $$"""
            using Trellis;

            namespace TestNamespace;

            {{attribute}}
            public partial class Widget : {{baseType}}
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        var diagnostic = diagnostics.Single(d => d.Id == "TRLS060");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Widget");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain(attributeInMessage);
    }

    [Fact]
    public void NonEmptyReasonCodeOverride_ReportsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [StringLength(5, Code = "widget.name.length")]
            public partial class Widget : RequiredString<Widget>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().NotContain(d => d.Id == "TRLS060");
    }

    [Fact]
    public void BothValidateAdditionalOverloads_Reports_TRLS061()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Widget : RequiredString<Widget>
            {
                static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
                {
                }

                static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        var diagnostic = diagnostics.Single(d => d.Id == "TRLS061");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Widget");
        diagnostics.Should().NotContain(d => d.Id == "TRLS062",
            "TRLS061 already rejects dual validation hooks, so a four-argument migration hint would be misleading");
    }

    [Fact]
    public void FourArgValidateAdditional_CompilesWithoutErrorsOrHookDiagnostics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Widget : RequiredString<Widget>
            {
                static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().NotContain(d => d.Id == "TRLS061");
        diagnostics.Should().NotContain(d => d.Id == "TRLS062");
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the four-argument validation hook should compile");
    }

    [Fact]
    public void ThreeArgValidateAdditional_Reports_TRLS062()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Widget : RequiredString<Widget>
            {
                static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
                {
                }
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        var diagnostic = diagnostics.Single(d => d.Id == "TRLS062");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Info);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Widget");
    }

    [Fact]
    public void NoValidateAdditional_ReportsNo_TRLS062()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The overwhelmingly common case. A value object with no custom rule has no failure of its
        // own to name, so suggesting the four-argument overload would be noise on nearly every type.
        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class Widget : RequiredString<Widget>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().NotContain(d => d.Id == "TRLS062");
    }

    [Fact]
    public void NegativeOnRequiredGuid_Reports_TRLS043()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Negative]
            public partial class OrderId : RequiredGuid<OrderId>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS043");
    }

    [Fact]
    public void PositiveAndNonNegative_Reports_TRLS044_Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Positive, NonNegative]
            public partial class Quantity : RequiredInt<Quantity>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS044");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS044");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Quantity");
    }

    [Fact]
    public void PositiveAndExplicitRange_Reports_TRLS045_Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Positive, Range(0, 100)]
            public partial class Quantity : RequiredInt<Quantity>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS045");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS045");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Quantity");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("[Positive]");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("[Range]");
    }

    [Fact]
    public void NonNegativeAndExplicitDecimalRange_Reports_TRLS045_Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [NonNegative, Range(0.0, 1000.0)]
            public partial class Amount : RequiredDecimal<Amount>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS045");
    }

    [Fact]
    public void RequiredDateTimeOffset_GeneratesWithoutDiagnostics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class SubmittedAt : RequiredDateTimeOffset<SubmittedAt>
            {
            }
            """;

        var diagnostics = RunGeneratorAndGetDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("RequiredDateTimeOffset is a recognised base in this release");
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();

    private static MetadataReference[] GetCoreOnlyMetadataReferences() =>
        GetMetadataReferences()
            .Where(static reference => reference.Display is null
                || !reference.Display.EndsWith("Trellis.Primitives.dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static Diagnostic[] RunGeneratorAndGetDiagnostics(
        string source,
        CancellationToken cancellationToken,
        MetadataReference[]? references = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorNestingTests",
            syntaxTrees: [syntaxTree],
            references: references ?? GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDriverDiagnostics,
            cancellationToken);

        return generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();
    }

    [Fact]
    public void InvalidIntRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(100, 1)]
            public partial class BadQuantity : RequiredInt<BadQuantity>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadQuantity");
    }

    [Fact]
    public void InvalidDecimalRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(999, 1)]
            public partial class BadPrice : RequiredDecimal<BadPrice>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadPrice");
    }

    [Fact]
    public void InvalidLongRange_Reports_TRLS033()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            [Range(1000, 1)]
            public partial class BadSequence : RequiredLong<BadSequence>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS033");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS033");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("BadSequence");
    }

    [Theory]
    [InlineData("0.0, 1e30")]
    [InlineData("0.0, 7.922816251426434e28")]
    [InlineData("-7.922816251426434e28, 0.0")]
    public void DecimalRangeExceedsDecimalRange_Reports_TRLS034(string bounds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var source = $$"""
            using Trellis;

            namespace TestNamespace;

            [Range({{bounds}})]
            public partial class TooBigPrice : RequiredDecimal<TooBigPrice>
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RequiredPartialClassGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        var diagnostics = generatorDriverDiagnostics
            .Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .ToArray();

        diagnostics.Should().ContainSingle(d => d.Id == "TRLS034");
        var diagnostic = diagnostics.Single(d => d.Id == "TRLS034");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("TooBigPrice");
    }

    [Theory]
    [MemberData(nameof(LegacyDecimalLiteralCases))]
    public void FormatDecimalLiteral_LegacyNet10_OracleInput_MatchesExpectedLiteral(string bits, string? expected)
    {
        var value = BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

        s_formatDecimalLiteral(value).Should().Be(expected, "binary64 input 0x{0} must retain its .NET 10 conversion", bits);
    }

    public static IEnumerable<TheoryDataRow<string, string?>> LegacyDecimalLiteralCases()
    {
        using var stream = typeof(RequiredPartialClassGeneratorDiagnosticsTests).Assembly
            .GetManifestResourceStream("TestData.LegacyDecimalLiterals.csv")
            ?? throw new InvalidOperationException("Missing embedded .NET 10 decimal oracle.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#'))
                continue;

            var columns = line.Split(',');
            yield return new(columns[0], columns[1] == "overflow" ? null : columns[1]);
        }
    }

    [Theory]
    [InlineData("1e-29", "0")]
    [InlineData("5e-29", "0")]
    [InlineData("1.2345678901234567", "1.23456789012346")]
    [InlineData("7.072999056928325e-10", "0.000000000707299905692832")]
    [InlineData("608192971259.4454", "608192971259.446")]
    [InlineData("-608192971259.4454", "-608192971259.446")]
    [InlineData("3.354019696523445e25", "33540196965234400000000000")]
    public void DecimalRange_CompilerHostConversion_UsesStableLiterals(string bound, string expectedLiteral)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = $$"""
            using Trellis;

            namespace TestNamespace;

            [Range({{bound}}, {{bound}})]
            public partial class ExactPrice : RequiredDecimal<ExactPrice>
            {
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDecimalConversionTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RequiredPartialClassGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDriverDiagnostics, cancellationToken);

        generatorDriverDiagnostics.Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var generated = driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();
        generated.Should().Contain($"if (value < {expectedLiteral}m)");
        generated.Should().Contain($"if (value > {expectedLiteral}m)");
    }
}