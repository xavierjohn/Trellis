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
                    Result.Ok(value)
                        .Ensure(
                            v => v >= -273.15m,
                            Error.InvalidInput.ForField("below_absolute_zero", fieldName ?? "temperature", detail: "Below absolute zero"))
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
    /// Every <c>Required*&lt;TSelf&gt;</c> scalar primitive base wraps a primitive that is already in the
    /// AOT-safe reader/writer set, so each must be discovered by the syntax predicate and emit a converter.
    /// The predicate previously hard-coded a subset of base names, silently skipping RequiredLong,
    /// RequiredBool, RequiredDateTime, and RequiredDateTimeOffset with no converter and no TRLS039.
    /// </summary>
    [Theory]
    [InlineData("RequiredLong", "Weight", "long")]
    [InlineData("RequiredBool", "IsActive", "bool")]
    [InlineData("RequiredDateTime", "OccurredAt", "System.DateTime")]
    [InlineData("RequiredDateTimeOffset", "SubmittedAt", "System.DateTimeOffset")]
    [InlineData("RequiredInt", "Quantity", "int")]
    [InlineData("RequiredDecimal", "Price", "decimal")]
    [InlineData("RequiredGuid", "OrderId", "System.Guid")]
    [InlineData("RequiredString", "FirstName", "string")]
    public void Every_Required_Scalar_Base_Is_Discovered_And_Uses_A_Closed_Validating_Reader(
        string baseType,
        string typeName,
        string primitiveType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var source = $$"""
            using Trellis;

            namespace TestNamespace;

            public partial class {{typeName}} : {{baseType}}<{{typeName}}>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);

        generatedSources.Should().Contain(
            s => s.Contains($"{typeName}JsonConverter")
                && s.Contains($"ValidatingJsonConverter<TestNamespace.{typeName}, {primitiveType}>")
                && s.Contains("Inner.Read(ref reader, typeToConvert, options)"),
            $"the generator should discover {typeName} and close the shared reader over its primitive");
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
    /// Scalar value objects wrapping a primitive that is not in the AOT-safe set
    /// (string, int, long, short, byte, bool, float, double, decimal, Guid,
    /// DateTime, DateTimeOffset) must trigger the TRLS039 diagnostic so users
    /// know to provide a custom JsonConverter. See issue #413.
    /// </summary>
    [Fact]
    public void Unsupported_Primitive_Emits_TRLS039_Diagnostic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public sealed class Duration : ScalarValueObject<Duration, System.TimeSpan>,
                IScalarValue<Duration, System.TimeSpan>
            {
                private Duration(System.TimeSpan value) : base(value) { }

                public static Result<Duration> TryCreate(System.TimeSpan value, string? fieldName = null) =>
                    Result.Ok(new Duration(value));
                public static Result<Duration> TryCreate(string? value, string? fieldName = null) =>
                    System.TimeSpan.TryParse(value, out var v)
                        ? Result.Ok(new Duration(v))
                        : Result.Fail<Duration>(Error.InvalidInput.ForField("Invalid", fieldName ?? "value"));
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS039",
            "value objects wrapping unsupported primitives must trigger TRLS039 so users add a custom JsonConverter");

        var trls039 = diagnostics.First(d => d.Id == "TRLS039");
        trls039.Severity.Should().Be(DiagnosticSeverity.Warning,
            "TRLS039 is advisory — users may legitimately ship a custom converter for unsupported primitives");
        trls039.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Contain("Duration").And.Contain("TimeSpan",
            "the diagnostic message must name the offending value object and primitive");
    }

    /// <summary>
    /// Regression guard for inspection finding M-2: the AOT-generated converter must integrate
    /// with <c>ValidationErrorsContext</c> so semantic validation failures surface as 422
    /// errors instead of being silently swallowed to <c>null</c>. Match runtime behavior
    /// bit-for-bit with <c>ScalarValueJsonConverterBase.Read</c>.
    /// </summary>
    [Fact]
    public void Generated_Converter_Reads_Field_Name_From_ValidationErrorsContext()
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
        var converter = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;

        converter.Should().Contain("Inner.Read(ref reader, typeToConvert, options)",
            "the shared reader resolves the live JSON property name");
    }

    [Fact]
    public void Generated_Converter_Passes_NonNull_FieldName_To_TryCreate()
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
        var converter = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;

        converter.Should().NotContain("TryCreate(primitiveValue, null)",
            "M-2: TryCreate must receive the resolved fieldName, not a literal null, so failures carry an actionable field reference");
        converter.Should().Contain("Inner.Read(ref reader, typeToConvert, options)",
            "the shared reader supplies TryCreate with the resolved field name");
    }

    [Fact]
    public void Generated_Converter_Records_Validation_Failure_In_ValidationErrorsContext()
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
        var converter = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;

        converter.Should().Contain("Inner.Read(ref reader, typeToConvert, options)",
            "the shared reader collects structured body failures rather than silently returning null");
    }

    [Fact]
    public void Generated_Converter_Falls_Back_To_Camel_Cased_Type_Name_When_PropertyName_Missing()
    {
        // AOT consumers that don't wire up the property-name TypeInfoResolver modifier (which
        // is reflection-based) still get a useful field reference in the validation error,
        // matching ScalarValueJsonConverterBase.GetDefaultFieldName().
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class OrderId : RequiredGuid<OrderId>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);
        var converter = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;

        converter.Should().Contain("ValidatingJsonConverter<TestNamespace.OrderId, System.Guid>",
            "the shared reader derives the default field name from the closed scalar type");
    }

    /// <summary>
    /// Regression guard for inspection finding M-2 (acronym camel-case parity): the AOT-generator
    /// must mirror <c>JsonNamingPolicy.CamelCase.ConvertName</c> bit-for-bit, including the
    /// acronym-block rule (<c>SKU → "sku"</c>, <c>URLValue → "urlValue"</c>,
    /// <c>IPAddress → "ipAddress"</c>, <c>XMLDocument → "xmlDocument"</c>). The naive
    /// "lowercase-first-character-only" implementation produces <c>"sKU"</c>/<c>"uRLValue"</c>
    /// /<c>"iPAddress"</c> and silently diverges from reflection mode for acronym-leading types.
    /// </summary>
    [Theory]
    [InlineData("SKU", "sku")]
    [InlineData("URLValue", "urlValue")]
    [InlineData("IPAddress", "ipAddress")]
    [InlineData("XMLDocument", "xmlDocument")]
    public void Generated_Converter_Camel_Cases_Acronym_Type_Names_Like_JsonNamingPolicy_CamelCase(
        string typeName,
        string expectedCamelCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var source = $$"""
            using Trellis;

            namespace TestNamespace;

            public partial class {{typeName}} : RequiredString<{{typeName}}>
            {
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);
        var converter = generatedSources.Should().ContainSingle(s => s.Contains($"{typeName}JsonConverter")).Subject;

        converter.Should().Contain($"ValidatingJsonConverter<TestNamespace.{typeName}, string>",
            $"the shared reader must derive '{expectedCamelCase}' from the original scalar type name");

        var naive = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
        if (!string.Equals(naive, expectedCamelCase, StringComparison.Ordinal))
        {
            converter.Should().NotContain($"\"{naive}\"",
                $"M-2: the naive lowercase-first-character form '{naive}' diverges from " +
                "JsonNamingPolicy.CamelCase and must not appear in the generated source");
        }
    }

    /// <summary>
    /// Regression guard for inspection finding M-2 (primitive read failure parity): when the JSON
    /// token has the wrong shape for the wrapped primitive, both generated and reflection-mode
    /// converters use the shared non-throwing primitive reader.
    /// </summary>
    [Fact]
    public void Generated_Converter_Uses_The_Shared_Reader_Without_Exception_Based_Primitive_Parsing()
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
        var converter = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;

        converter.Should().Contain("Inner.Read(ref reader, typeToConvert, options)");
        converter.Should().NotContain("catch");
        converter.Should().NotContain("reader.GetGuid()");
    }

    /// <summary>
    /// Generated converters must never call reflection-based <c>JsonSerializer.Deserialize</c>
    /// or <c>JsonSerializer.Serialize</c> overloads — those are annotated
    /// <c>[RequiresUnreferencedCode]</c>/<c>[RequiresDynamicCode]</c> and produce
    /// IL2026/IL3050 under <c>PublishAot=true</c>. Mixed fixture (one supported +
    /// one unsupported primitive) verifies the unsupported type is skipped while the
    /// supported type still generates an AOT-safe converter. See issue #413.
    /// </summary>
    [Fact]
    public void Unsupported_Primitive_Is_Skipped_And_No_Reflection_Based_JsonSerializer_Calls_Are_Emitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using Trellis;

            namespace TestNamespace;

            public partial class OrderId : RequiredGuid<OrderId>
            {
            }

            public sealed class Duration : ScalarValueObject<Duration, System.TimeSpan>,
                IScalarValue<Duration, System.TimeSpan>
            {
                private Duration(System.TimeSpan value) : base(value) { }

                public static Result<Duration> TryCreate(System.TimeSpan value, string? fieldName = null) =>
                    Result.Ok(new Duration(value));
                public static Result<Duration> TryCreate(string? value, string? fieldName = null) =>
                    System.TimeSpan.TryParse(value, out var v)
                        ? Result.Ok(new Duration(v))
                        : Result.Fail<Duration>(Error.InvalidInput.ForField("Invalid", fieldName ?? "value"));
            }
            """;

        var generatedSources = RunGenerator(source, cancellationToken);
        var combined = string.Concat(generatedSources);

        combined.Should().Contain("OrderIdJsonConverter",
            "supported primitives must still generate a converter alongside unsupported ones");
        combined.Should().NotContain("DurationJsonConverter",
            "value objects with unsupported primitives must be skipped entirely (issue #413) — no converter at all");
        combined.Should().NotContain("JsonSerializer.Deserialize",
            "AOT-incompatible reflection-based deserialization must never be emitted (issue #413)");
        combined.Should().NotContain("JsonSerializer.Serialize(writer",
            "AOT-incompatible reflection-based serialization must never be emitted (issue #413)");
    }

    /// <summary>
    /// Regression guard: supported primitives must still emit fully AOT-safe converters
    /// using <c>Utf8JsonReader</c>/<c>Utf8JsonWriter</c> APIs and never fall back to
    /// reflection-based <c>JsonSerializer</c> overloads.
    /// </summary>
    [Fact]
    public void Supported_Primitive_Converter_Uses_Direct_Reader_Writer_Apis()
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

        var converterSource = generatedSources.Should().ContainSingle(s => s.Contains("OrderIdJsonConverter")).Subject;
        converterSource.Should().Contain("ValidatingJsonConverter<TestNamespace.OrderId, System.Guid>",
            "the shared closed reader uses non-throwing typed primitive APIs without dynamic construction");
        converterSource.Should().NotContain("JsonSerializer.Deserialize",
            "supported primitives must never fall back to reflection-based JsonSerializer (issue #413)");
        converterSource.Should().NotContain("JsonSerializer.Serialize(writer",
            "supported primitives must never fall back to reflection-based JsonSerializer (issue #413)");
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

                    public static Result<Quantity> TryCreate(int value, string? fieldName = null) => Result.Ok(new Quantity(value));
                    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
                        int.TryParse(value, out var v) ? Result.Ok(new Quantity(v)) : Result.Fail<Quantity>(Error.InvalidInput.ForField("Invalid", fieldName ?? "value"));
                }
            }

            namespace Support
            {
                public sealed class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
                {
                    private Quantity(int value) : base(value) { }

                    public static Result<Quantity> TryCreate(int value, string? fieldName = null) => Result.Ok(new Quantity(value));
                    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
                        int.TryParse(value, out var v) ? Result.Ok(new Quantity(v)) : Result.Fail<Quantity>(Error.InvalidInput.ForField("Invalid", fieldName ?? "value"));
                }
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator should use unique converter names for scalar values that share a simple type name");
    }

    /// <summary>
    /// A <c>[GenerateScalarValueConverters]</c> context that declares no <c>[JsonSerializable]</c>
    /// of its own is skipped entirely by System.Text.Json's generator, which then fails the build
    /// with CS0534. Trellis cannot supply the attribute (generators cannot see each other's output),
    /// so it must at least say so.
    /// </summary>
    [Fact]
    public void SerializerContext_Without_JsonSerializable_Emits_TRLS059_Diagnostic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Text.Json.Serialization;
            using Trellis;
            using Trellis.Asp;

            namespace TestNamespace;

            public partial class OrderId : RequiredGuid<OrderId>
            {
            }

            [GenerateScalarValueConverters]
            public partial class AppContext : JsonSerializerContext
            {
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS059",
            "a context with no hand-written [JsonSerializable] is always broken, and CS0534 alone does not explain why");

        var trls059 = diagnostics.First(d => d.Id == "TRLS059");
        trls059.Severity.Should().Be(DiagnosticSeverity.Warning,
            "the compiler already fails the build; TRLS059 exists to explain the cause, not to duplicate the error");
        trls059.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Contain("AppContext",
            "the diagnostic must name the offending context");
    }

    /// <summary>
    /// The diagnostic must stay quiet once the context carries a hand-written
    /// <c>[JsonSerializable]</c>, which is the supported configuration.
    /// </summary>
    [Fact]
    public void SerializerContext_With_JsonSerializable_Does_Not_Emit_TRLS059()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Text.Json.Serialization;
            using Trellis;
            using Trellis.Asp;

            namespace TestNamespace;

            public partial class OrderId : RequiredGuid<OrderId>
            {
            }

            public class OrderDto
            {
                public string? Name { get; set; }
            }

            [GenerateScalarValueConverters]
            [JsonSerializable(typeof(OrderDto))]
            public partial class AppContext : JsonSerializerContext
            {
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Should().NotContain(d => d.Id == "TRLS059",
            "the context is correctly configured, so the diagnostic would be noise");
    }

    /// <summary>
    /// The context is broken by the missing <c>[JsonSerializable]</c> alone, independently of
    /// whether this compilation declares any value objects. Value objects commonly live in a
    /// referenced domain assembly while the context lives in the web project, so the diagnostic
    /// must not depend on finding one locally.
    /// </summary>
    [Fact]
    public void SerializerContext_Without_JsonSerializable_Emits_TRLS059_When_No_ValueObjects_Are_Declared()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System.Text.Json.Serialization;
            using Trellis.Asp;

            namespace TestNamespace;

            [GenerateScalarValueConverters]
            public partial class AppContext : JsonSerializerContext
            {
            }
            """;

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken);

        diagnostics.Should().Contain(d => d.Id == "TRLS059",
            "the context still fails with CS0534 even though no value object is declared in this compilation");
    }

    private static List<string> RunGenerator(string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GeneratorTestReferences.All;

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
        string source,
        CancellationToken cancellationToken,
        bool includeCoreGenerator = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GeneratorTestReferences.All;

        Compilation compilation = CSharpCompilation.Create(
            assemblyName: "ScalarValueGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var coreDiagnostics = Enumerable.Empty<Diagnostic>();
        if (includeCoreGenerator)
        {
            var coreGenerator = new SourceGenerator.RequiredPartialClassGenerator();
            GeneratorDriver coreDriver = CSharpGeneratorDriver.Create(coreGenerator);
            coreDriver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out compilation,
                out var generatedCoreDiagnostics,
                cancellationToken);
            coreDiagnostics = generatedCoreDiagnostics;
        }

        var generator = new ScalarValueJsonConverterGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics,
            cancellationToken);

        var allDiagnostics = coreDiagnostics
            .Concat(diagnostics)
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
}