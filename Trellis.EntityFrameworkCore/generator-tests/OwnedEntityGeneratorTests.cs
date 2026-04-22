namespace Trellis.EntityFrameworkCore.Generator.Tests;

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Tests for <see cref="OwnedEntityGenerator"/> diagnostics and source generation.
/// </summary>
public class OwnedEntityGeneratorTests
{
    #region TRLS038 — does not inherit from ValueObject

    /// <summary>
    /// A plain class decorated with [OwnedEntity] — not inheriting ValueObject — must emit TRLS038.
    /// </summary>
    [Fact]
    public void OwnedEntity_On_NonValueObject_Class_Should_Emit_TRLS038()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            namespace Trellis { public abstract class ValueObject { } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace;

            [Trellis.EntityFrameworkCore.OwnedEntity]
            public partial class PlainClass
            {
                public string Name { get; private set; } = null!;

                public PlainClass(string name) { Name = name; }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038")
            .Should().ContainSingle()
            .Which.GetMessage(CultureInfo.InvariantCulture).Should().Contain("PlainClass");
    }

    /// <summary>
    /// TRLS038 message must name the type that does not inherit from ValueObject.
    /// </summary>
    [Fact]
    public void TRLS038_Message_Should_Include_TypeName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            namespace Trellis { public abstract class ValueObject { } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace;

            [Trellis.EntityFrameworkCore.OwnedEntity]
            public partial class NotAValueObject
            {
                public NotAValueObject(string x) { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038")
            .Should().ContainSingle()
            .Which.GetMessage(CultureInfo.InvariantCulture).Should().Contain("NotAValueObject");
    }

    /// <summary>
    /// TRLS038 must be an error, not a warning.
    /// </summary>
    [Fact]
    public void TRLS038_Should_Be_Error_Severity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            namespace Trellis { public abstract class ValueObject { } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace;

            [Trellis.EntityFrameworkCore.OwnedEntity]
            public partial class PlainClass
            {
                public PlainClass(string name) { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038")
            .Should().ContainSingle()
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    /// <summary>
    /// When TRLS038 fires, no source should be generated (generation is skipped).
    /// </summary>
    [Fact]
    public void OwnedEntity_On_NonValueObject_Should_Not_Generate_Constructor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            namespace Trellis { public abstract class ValueObject { } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace;

            [Trellis.EntityFrameworkCore.OwnedEntity]
            public partial class PlainClass
            {
                public string Name { get; private set; } = null!;

                public PlainClass(string name) { Name = name; }
            }
            """;

        var (generatedSources, _, _) = RunGenerator(source, cancellationToken);

        generatedSources.Should().BeEmpty("generation must be skipped when TRLS038 fires");
    }

    /// <summary>
    /// When TRLS038 fires (wrong base class), TRLS036 (non-partial) must NOT also fire —
    /// only one diagnostic per type is emitted due to the early-return ordering.
    /// </summary>
    [Fact]
    public void OwnedEntity_On_NonValueObject_NonPartial_Should_Emit_Only_TRLS038()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            namespace Trellis { public abstract class ValueObject { } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace;

            [Trellis.EntityFrameworkCore.OwnedEntity]
            public class NotPartialNotValueObject
            {
                public NotPartialNotValueObject(string name) { }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038").Should().ContainSingle();
        diagnostics.Where(d => d.Id == "TRLS036").Should().BeEmpty();
    }

    #endregion

    #region Happy path — ValueObject-derived types

    /// <summary>
    /// A class inheriting directly from ValueObject must NOT emit TRLS038.
    /// </summary>
    [Fact]
    public void OwnedEntity_On_ValueObject_Derived_Class_Should_Not_Emit_TRLS038()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Collections.Generic;
            namespace Trellis { public abstract class ValueObject { protected abstract IEnumerable<System.IComparable?> GetEqualityComponents(); } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace
            {
                [Trellis.EntityFrameworkCore.OwnedEntity]
                public partial class Address : Trellis.ValueObject
                {
                    public string Street { get; private set; } = null!;

                    public Address(string street) { Street = street; }

                    protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                    {
                        yield return Street;
                    }
                }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038").Should().BeEmpty();
    }

    /// <summary>
    /// A class inheriting directly from ValueObject should have a constructor generated.
    /// </summary>
    [Fact]
    public void OwnedEntity_On_ValueObject_Derived_Class_Should_Generate_Constructor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Collections.Generic;
            namespace Trellis { public abstract class ValueObject { protected abstract IEnumerable<System.IComparable?> GetEqualityComponents(); } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace
            {
                [Trellis.EntityFrameworkCore.OwnedEntity]
                public partial class Address : Trellis.ValueObject
                {
                    public string Street { get; private set; } = null!;

                    public Address(string street) { Street = street; }

                    protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents()
                    {
                        yield return Street;
                    }
                }
            }
            """;

        var (generatedSources, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSources.Should().ContainSingle(s => s.Contains("private Address()"),
            "the generator should emit a private parameterless constructor");
        generatedSources.Should().Contain(s => s.Contains("Street = null!;"),
            "the constructor should initialize the reference-type property with null!");
    }

    /// <summary>
    /// A class inheriting from a class that itself inherits ValueObject (indirect inheritance)
    /// must NOT emit TRLS038.
    /// </summary>
    [Fact]
    public void OwnedEntity_On_Indirect_ValueObject_Derived_Class_Should_Not_Emit_TRLS038()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Collections.Generic;
            namespace Trellis { public abstract class ValueObject { protected abstract IEnumerable<System.IComparable?> GetEqualityComponents(); } }
            namespace Trellis.EntityFrameworkCore { public sealed class OwnedEntityAttribute : System.Attribute { } }

            namespace TestNamespace
            {
                public abstract class BaseAddress : Trellis.ValueObject
                {
                    public string Country { get; private set; } = null!;
                    protected BaseAddress(string country) { Country = country; }
                    protected override System.Collections.Generic.IEnumerable<System.IComparable?> GetEqualityComponents() { yield return Country; }
                }

                [Trellis.EntityFrameworkCore.OwnedEntity]
                public partial class DerivedAddress : BaseAddress
                {
                    public string Street { get; private set; } = null!;
                    public DerivedAddress(string country, string street) : base(country) { Street = street; }
                }
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source, cancellationToken);

        diagnostics.Where(d => d.Id == "TRLS038").Should().BeEmpty();
    }

    #endregion

    private static (List<string> Sources, IReadOnlyList<Diagnostic> Diagnostics, List<string> HintNames) RunGenerator(
        string source, System.Threading.CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "OwnedEntityGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new OwnedEntityGenerator();
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

        return references.ToArray();
    }
}
