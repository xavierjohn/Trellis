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
                        .Ensure(
                            v => v >= -273.15m,
                            Error.InvalidInput.ForField(fieldName ?? "temperature", "below_absolute_zero", "Below absolute zero"))
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
                        : Result.Fail<Duration>(Error.InvalidInput.ForField(fieldName ?? "value", "Invalid"));
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

        converter.Should().Contain("ValidationErrorsContext.CurrentPropertyName",
            "M-2: generated Read must consult ValidationErrorsContext for the property name to mirror reflection-mode behavior");
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
        converter.Should().Contain("TryCreate(primitiveValue, fieldName",
            "M-2: TryCreate must receive the resolved fieldName so the resulting Error.InvalidInput points at the right property");
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

        converter.Should().Contain("ValidationErrorsContext.AddError",
            "M-2: validation failures must be reported via ValidationErrorsContext so the request middleware can surface a 422 response");
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

        converter.Should().Contain("\"orderId\"",
            "M-2: the generated default field name must be the camel-cased simple type name to match ScalarValueJsonConverterBase.GetDefaultFieldName()");
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

        converter.Should().Contain($"\"{expectedCamelCase}\"",
            $"M-2: the generated default field name for '{typeName}' must be '{expectedCamelCase}' " +
            "(the JsonNamingPolicy.CamelCase.ConvertName output) to match reflection-mode behavior");

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
    /// token has the wrong shape for the wrapped primitive (e.g. a number for a string VO, or a
    /// non-Guid string for a Guid VO), the typed <c>Utf8JsonReader</c> getters throw
    /// <c>FormatException</c>/<c>InvalidOperationException</c>. Reflection mode catches both via
    /// <c>PrimitiveJsonReader.TryRead</c> and records a 422 instead of letting the exception escape
    /// as a <c>JsonException</c>. The AOT generator must do the same; otherwise AOT consumers see
    /// a 400/500 from the deserializer instead of the same 422 reflection-mode produces.
    /// </summary>
    [Fact]
    public void Generated_Converter_Wraps_Primitive_Read_In_Try_Catch_For_FormatException_And_InvalidOperationException()
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

        converter.Should().Contain("FormatException",
            "M-2: generated Read must catch FormatException so invalid tokens for the wrapped primitive surface as 422 instead of escaping as JsonException");
        converter.Should().Contain("InvalidOperationException",
            "M-2: generated Read must catch InvalidOperationException so invalid tokens for the wrapped primitive surface as 422 instead of escaping as JsonException");
        converter.Should().Contain("is not a valid",
            "M-2: generated Read must record the same 'is not a valid {Type}' error message that PrimitiveJsonReader.TryRead emits in reflection mode");
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
                        : Result.Fail<Duration>(Error.InvalidInput.ForField(fieldName ?? "value", "Invalid"));
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
        converterSource.Should().Contain("reader.GetGuid()",
            "supported Guid primitives must use the direct Utf8JsonReader API");
        converterSource.Should().NotContain("JsonSerializer.Deserialize",
            "supported primitives must never fall back to reflection-based JsonSerializer (issue #413)");
        converterSource.Should().NotContain("JsonSerializer.Serialize(writer",
            "supported primitives must never fall back to reflection-based JsonSerializer (issue #413)");
    }

    [Fact]
    public void Generator_NestedContext_ProducesValidNestedPartialClassDeclaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Trellis;
            using Trellis.Asp;

            namespace MyApi;

            public partial class Outer
            {
                [GenerateScalarValueConverters]
                [JsonSerializable(typeof(EmailAddress))]
                public partial class InnerContext : JsonSerializerContext
                {
                    public InnerContext() : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                    public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                }
            }

            public partial class EmailAddress : RequiredString<EmailAddress>;
            """;

        var (generatedSources, diagnostics, hintNames) = RunGeneratorWithDiagnostics(source, cancellationToken, includeCoreGenerator: true);

        generatedSources.Should().NotBeEmpty("the generator should emit converter and context sources");
        hintNames.Should().Contain("MyApi__Outer__InnerContext__ValueObjects.g.cs",
            "the generated hint name should include the namespace and containing-type chain");
        var contextSource = generatedSources.Should()
            .ContainSingle(s => s.Contains("partial class InnerContext"))
            .Subject;
        contextSource.Should().Contain("partial class Outer",
            "nested JsonSerializerContext declarations must be generated under their containing partial type");

        var outerIndex = contextSource.IndexOf("partial class Outer", StringComparison.Ordinal);
        var innerIndex = contextSource.IndexOf("partial class InnerContext", StringComparison.Ordinal);
        innerIndex.Should().BeGreaterThan(outerIndex,
            "the generated context declaration must be nested inside the generated Outer partial declaration");

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated nested partial declaration should compile without C# errors");
    }

    [Fact]
    public void Generator_NestedContext_PreservesContainingTypeDeclarations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Trellis;
            using Trellis.Asp;

            namespace MyApi;

            internal partial record class OuterRecord
            {
                private partial struct InnerStruct
                {
                    [GenerateScalarValueConverters]
                    [JsonSerializable(typeof(EmailAddress))]
                    public partial class InnerContext : JsonSerializerContext
                    {
                        public InnerContext() : base(new JsonSerializerOptions())
                        {
                        }

                        protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                        public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                    }
                }
            }

            public partial class EmailAddress : RequiredString<EmailAddress>;
            """;

        var (generatedSources, diagnostics, _) = RunGeneratorWithDiagnostics(source, cancellationToken, includeCoreGenerator: true);
        var contextSource = generatedSources.Should()
            .ContainSingle(s => s.Contains("partial class InnerContext"))
            .Subject;

        contextSource.Should().Contain("internal partial record class OuterRecord",
            "the generated containing record declaration must match the consumer declaration");
        contextSource.Should().Contain("private partial struct InnerStruct",
            "the generated containing struct declaration must match the consumer declaration");

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("matching containing type declarations should compile for non-class and non-public containers");
    }

    [Fact]
    public void Generator_DuplicateNamedContextsInDifferentNamespaces_ProduceDistinctHintNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Trellis;
            using Trellis.Asp;

            namespace MyApi
            {
                public partial class EmailAddress : RequiredString<EmailAddress>;
            }

            namespace MyApi.V1
            {
                [GenerateScalarValueConverters]
                [JsonSerializable(typeof(MyApi.EmailAddress))]
                public partial class AppContext : JsonSerializerContext
                {
                    public AppContext() : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                    public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                }
            }

            namespace MyApi.V2
            {
                [GenerateScalarValueConverters]
                [JsonSerializable(typeof(MyApi.EmailAddress))]
                public partial class AppContext : JsonSerializerContext
                {
                    public AppContext() : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                    public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                }
            }
            """;

        var (generatedSources, diagnostics, hintNames) = RunGeneratorWithDiagnostics(source, cancellationToken, includeCoreGenerator: true);
        var contextSources = generatedSources
            .Zip(hintNames, static (sourceText, hintName) => new { SourceText = sourceText, HintName = hintName })
            .Where(static generated => generated.HintName.EndsWith("ValueObjects.g.cs", StringComparison.Ordinal))
            .ToList();

        contextSources.Should().HaveCount(2,
            "each same-named context in a different namespace should get its own generated source file");
        contextSources.Select(static generated => generated.HintName)
            .Should().OnlyHaveUniqueItems("fully-qualified context hint names should not collide");
        contextSources.Select(static generated => generated.HintName)
            .Should().Equal([
                "MyApi__V1__AppContext__ValueObjects.g.cs",
                "MyApi__V2__AppContext__ValueObjects.g.cs"]);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("same-named JsonSerializerContext types in different namespaces should compile together");
    }

    [Fact]
    public void Generator_AmbiguousContextPaths_ProduceDistinctHintNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        const string source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Trellis;
            using Trellis.Asp;

            namespace Shared
            {
                public partial class EmailAddress : RequiredString<EmailAddress>;
            }

            namespace A.B
            {
                [GenerateScalarValueConverters]
                [JsonSerializable(typeof(Shared.EmailAddress))]
                public partial class AppContext : JsonSerializerContext
                {
                    public AppContext() : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                    public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                }
            }

            namespace A__B
            {
                [GenerateScalarValueConverters]
                [JsonSerializable(typeof(Shared.EmailAddress))]
                public partial class AppContext : JsonSerializerContext
                {
                    public AppContext() : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                    public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                }
            }

            namespace GenericCollision
            {
                public partial class Outer
                {
                    [GenerateScalarValueConverters]
                    [JsonSerializable(typeof(Shared.EmailAddress))]
                    public partial class AppContext : JsonSerializerContext
                    {
                        public AppContext() : base(new JsonSerializerOptions())
                        {
                        }

                        protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                        public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                    }
                }

                public partial class Outer<T>
                {
                    [GenerateScalarValueConverters]
                    [JsonSerializable(typeof(Shared.EmailAddress))]
                    public partial class AppContext : JsonSerializerContext
                    {
                        public AppContext() : base(new JsonSerializerOptions())
                        {
                        }

                        protected override JsonSerializerOptions GeneratedSerializerOptions => new();

                        public override JsonTypeInfo GetTypeInfo(Type type) => throw new NotSupportedException();
                    }
                }
            }
            """;

        var (_, diagnostics, hintNames) = RunGeneratorWithDiagnostics(source, cancellationToken, includeCoreGenerator: true);
        var contextHintNames = hintNames
            .Where(static hintName => hintName.EndsWith("ValueObjects.g.cs", StringComparison.Ordinal))
            .ToList();

        contextHintNames.Should().HaveCount(4,
            "dotted namespaces, underscore namespaces, and generic containing types should each get their own source");
        contextHintNames.Should().OnlyHaveUniqueItems("hint-name path encoding should be unambiguous");
        contextHintNames.Should().BeEquivalentTo([
            "A__B__AppContext__ValueObjects.g.cs",
            "A_u005F_u005FB__AppContext__ValueObjects.g.cs",
            "GenericCollision__Outer__AppContext__ValueObjects.g.cs",
            "GenericCollision__Outer_u00601__AppContext__ValueObjects.g.cs"]);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("ambiguously encoded context paths should compile together without AddSource collisions");
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
                        int.TryParse(value, out var v) ? Result.Ok(new Quantity(v)) : Result.Fail<Quantity>(Error.InvalidInput.ForField(fieldName ?? "value", "Invalid"));
                }
            }

            namespace Support
            {
                public sealed class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
                {
                    private Quantity(int value) : base(value) { }

                    public static Result<Quantity> TryCreate(int value, string? fieldName = null) => Result.Ok(new Quantity(value));
                    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
                        int.TryParse(value, out var v) ? Result.Ok(new Quantity(v)) : Result.Fail<Quantity>(Error.InvalidInput.ForField(fieldName ?? "value", "Invalid"));
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
        string source,
        CancellationToken cancellationToken,
        bool includeCoreGenerator = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = GetMetadataReferences();

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

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Concat([
                typeof(RequiredGuid<>).Assembly.Location,
                typeof(ScalarValueObject<,>).Assembly.Location,
                typeof(Trellis.Asp.ValidationErrorsContext).Assembly.Location,
                typeof(System.Text.Json.JsonSerializer).Assembly.Location,
                typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}
