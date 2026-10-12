namespace Trellis.AspSourceGenerator.Tests;

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trellis;

/// <summary>
/// Tests for <see cref="PathTrackingRegistryGenerator"/> (issue #664): the compile-time DTO graph walk
/// must emit exactly the closed path-tracking registrations the reflection pipeline would build at
/// runtime, so Native AOT and reflection agree on which properties are wrapped.
/// </summary>
public class PathTrackingRegistryGeneratorTests
{
    private const string ValueObject = """
        using Trellis;

        public sealed class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
        {
            private Email(string value) : base(value) { }

            public static Result<Email> TryCreate(string? value, string? fieldName = null) =>
                Result.Ok(new Email(value!));
        }
        """;

    private const string CompositeValueObject = """
        [global::System.Text.Json.Serialization.JsonConverter(typeof(global::Trellis.Primitives.CompositeValueObjectJsonConverter<Address>))]
        public sealed class Address : global::Trellis.ValueObject
        {
            private Address(string street, string city) { Street = street; City = city; }

            public string Street { get; private set; } = string.Empty;

            public string City { get; private set; } = string.Empty;

            public static global::Trellis.Result<Address> TryCreate(string street, string city, string? fieldName = null) =>
                global::Trellis.Result.Ok(new Address(street, city));

            protected override void GetEqualityComponents(ref global::Trellis.EqualityComponents components)
            {
                components.Add(Street);
                components.Add(City);
            }
        }
        """;

    private const string NestingCompositeValueObject = """
        [global::System.Text.Json.Serialization.JsonConverter(typeof(global::Trellis.Primitives.CompositeValueObjectJsonConverter<NestingAddress>))]
        public sealed class NestingAddress : global::Trellis.ValueObject
        {
            private NestingAddress(global::TestNamespace.InnerDto inner) { Inner = inner; }

            public global::TestNamespace.InnerDto Inner { get; private set; } = default!;

            protected override void GetEqualityComponents(ref global::Trellis.EqualityComponents components) =>
                components.Add(Inner.Email.Value);
        }
        """;

    [Fact]
    public void Scalar_root_registers_a_closed_collecting_converter()
    {
        var source = $$"""
            {{ValueObject}}

            [global::System.Text.Json.Serialization.JsonSerializable(typeof(Email))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            """;

        RunGenerator(source).Should().Contain("RegisterScalar<global::Email, string>()");
    }

    [Fact]
    public void Composite_root_registers_its_result_based_converter()
    {
        var source = $$"""
            {{CompositeValueObject}}

            [global::System.Text.Json.Serialization.JsonSerializable(typeof(Address))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            """;

        RunGenerator(source).Should().Contain(
            "RegisterResultConverter(new global::Trellis.Primitives.CompositeValueObjectJsonConverter<global::Address>(), scalar: false)");
    }

    [Fact]
    public void Optional_enum_registers_both_collecting_scalar_shapes()
    {
        const string source = """
            [global::System.Text.Json.Serialization.JsonConverter(typeof(global::Trellis.RequiredEnumJsonConverter<State>))]
            public partial class State : global::Trellis.RequiredEnum<State>, global::Trellis.IScalarValue<State, string>
            {
                public static readonly State Draft = new();
            }
            public sealed record Command(global::Trellis.Maybe<State> State);
            [global::System.Text.Json.Serialization.JsonSerializable(typeof(Command))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            """;

        RunGenerator(source).Should().Contain(
            "RegisterScalar<global::State, string>(new global::Trellis.RequiredEnumJsonConverter<global::State>())");
    }

    [Theory]
    [InlineData("RequiredString", "string")]
    [InlineData("RequiredGuid", "global::System.Guid")]
    [InlineData("RequiredInt", "int")]
    [InlineData("RequiredLong", "long")]
    [InlineData("RequiredDecimal", "decimal")]
    [InlineData("RequiredBool", "bool")]
    [InlineData("RequiredDateTime", "global::System.DateTime")]
    [InlineData("RequiredDateTimeOffset", "global::System.DateTimeOffset")]
    [InlineData("RequiredEnum", "string")]
    public void Local_required_scalar_registers_before_the_core_generator_adds_its_interface(
        string baseType, string primitiveType)
    {
        var converter = baseType == "RequiredEnum" ? "RequiredEnumJsonConverter" : "ParsableJsonConverter";
        var source = $$"""
            [global::System.Text.Json.Serialization.JsonConverter(typeof(global::Trellis.{{converter}}<LocalValue>))]
            public partial class LocalValue : global::Trellis.{{baseType}}<LocalValue> { }
            public sealed record Command(LocalValue Value);
            [global::System.Text.Json.Serialization.JsonSerializable(typeof(Command))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterProperty<global::LocalValue>()");
        generated.Should().Contain($"RegisterScalar<global::LocalValue, {primitiveType}>(");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RegisterValidation_StructScalar_ReachableShape_DoesNotEmitClassConstrainedCalls(
        bool resultConverter, bool optional)
    {
        var attribute = resultConverter
            ? "[global::System.Text.Json.Serialization.JsonConverter(typeof(ManualScalarConverter))]"
            : string.Empty;
        var converter = resultConverter
            ? """
              public sealed class ManualScalarConverter : global::Trellis.ResultJsonConverter<ManualScalar>
              {
                  public override global::Trellis.Result<ManualScalar> TryRead(
                      ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type type,
                      global::System.Text.Json.JsonSerializerOptions options) =>
                      ManualScalar.TryCreate(reader.GetInt32());
                  public override void Write(global::System.Text.Json.Utf8JsonWriter writer,
                      ManualScalar value, global::System.Text.Json.JsonSerializerOptions options) =>
                      writer.WriteNumberValue(value.Value);
              }
              """
            : string.Empty;
        var root = optional ? "global::Trellis.Maybe<ManualScalar>" : "ManualScalar";
        var source = $$"""
            {{attribute}}
            public readonly record struct ManualScalar(int Value) : global::Trellis.IScalarValue<ManualScalar, int>
            {
                public static global::Trellis.Result<ManualScalar> TryCreate(int value, string? fieldName = null) =>
                    global::Trellis.Result.Ok(new ManualScalar(value));
                public static global::Trellis.Result<ManualScalar> TryCreate(string? value, string? fieldName = null) =>
                    global::Trellis.Result.Ok(new ManualScalar(int.Parse(value!)));
            }
            {{converter}}
            [global::System.Text.Json.Serialization.JsonSerializable(typeof({{root}}))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext
            {
                public AppContext() : base(null) { }
                protected override global::System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(
                    global::System.Type type) => null;
            }
            """;

        var generated = RunGeneratorOrNull(source, checkCompilation: true);

        if (resultConverter)
        {
            generated.Should().NotBeNull();
            generated.Should().NotContain("RegisterScalar<global::ManualScalar");
            generated.Should().Contain("RegisterResultConverter(new global::ManualScalarConverter(), scalar: true)");
        }
        else
        {
            generated.Should().BeNull("plain struct scalar roots have no class-constrained validation registration");
        }
    }

    [Theory]
    [InlineData("private", "public", "")]
    [InlineData("public", "private", "")]
    [InlineData("public", "public", "int unused")]
    public void RegisterValidation_ResultConverter_NotConstructibleFromRegistry_PreservesConsumerCompilation(
        string converterAccessibility, string constructorAccessibility, string parameters)
    {
        var source = CreateNestedConverterSource(converterAccessibility, constructorAccessibility, parameters);

        RunGeneratorOrNull(source, checkCompilation: true).Should().BeNull(
            "a closed registration cannot construct the application's inaccessible or parameterized converter");
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("internal", "internal")]
    [InlineData("protected internal", "public")]
    public void RegisterValidation_ResultConverter_AccessibleParameterlessConstructor_RegistersTheConverter(
        string converterAccessibility, string constructorAccessibility)
    {
        var source = CreateNestedConverterSource(converterAccessibility, constructorAccessibility, string.Empty);

        RunGeneratorOrNull(source, checkCompilation: true).Should().Contain(
            "RegisterScalar<global::Sample, int>(new global::Sample.Converter())");
    }

    [Fact]
    public void Collection_property_of_dtos_containing_a_value_object_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(List<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterProperty<global::Email>()");
        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
        generated.Should().Contain("[ModuleInitializer]");
    }

    [Fact]
    public void Scalar_property_is_registered_for_effective_json_name_tracking()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Text.Json.Serialization;

                public sealed record Command(
                    [property: JsonPropertyName("primary_email")] Email Contact);

                [JsonSerializable(typeof(Command))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterProperty<global::Email>()");
    }

    [Fact]
    public void Array_root_is_walked()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(List<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand[]))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
    }

    [Fact]
    public void Inherited_collection_property_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public abstract class BaseCommand
                {
                    public List<MemberDto> Members { get; init; } = new();
                }

                public sealed class TeamCommand : BaseCommand { }

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
    }

    /// <summary>
    /// §8.2(b) — the generator predicate must move in lockstep with the runtime install gate. Under
    /// Native AOT the generated registrations are the <em>only</em> path, because
    /// <c>CreatePathTrackingContainerConverter</c> returns <see langword="null"/> once
    /// <c>RuntimeFeature.IsDynamicCodeSupported</c> is false. A composite value object built entirely
    /// from primitives contains no <c>IScalarValue</c>, so before the widening it was skipped here and
    /// the fix would have worked in reflection mode and silently not under AOT.
    /// </summary>
    [Fact]
    public void Primitive_only_composite_value_object_property_is_registered()
    {
        var source = $$"""
            {{CompositeValueObject}}

            namespace TestNamespace
            {
                using System.Text.Json.Serialization;

                public sealed record OrderCommand(Address ShipTo);

                [JsonSerializable(typeof(OrderCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterObject<global::Address>()");
    }

    [Fact]
    public void Collection_of_primitive_only_composite_value_objects_is_registered()
    {
        var source = $$"""
            {{CompositeValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record RouteCommand(List<Address> Stops);

                [JsonSerializable(typeof(RouteCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::Address>, global::Address>()");
    }

    /// <summary>
    /// The parity rule runs in both directions. A composite value object owns its JSON shape through
    /// its own converter, so the runtime modifier sees an empty <c>JsonTypeInfo.Properties</c> and never
    /// installs wrappers inside it. Registering its inner properties would be over-registration — AOT
    /// wrapping something reflection leaves alone.
    /// </summary>
    [Fact]
    public void The_walk_does_not_descend_into_a_composite_value_object()
    {
        var source = $$"""
            {{ValueObject}}
            {{NestingCompositeValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record InnerDto(Email Email);

                public sealed record ShipmentCommand(NestingAddress ShipTo);

                [JsonSerializable(typeof(ShipmentCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterObject<global::NestingAddress>()");
        generated.Should().NotContain("RegisterObject<global::TestNamespace.InnerDto>()");
    }

    [Fact]
    public void Nested_object_property_containing_a_value_object_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Text.Json.Serialization;

                public sealed record AddressDto(Email Email);

                public sealed record PersonCommand(AddressDto Contact);

                [JsonSerializable(typeof(PersonCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterObject<global::TestNamespace.AddressDto>()");
    }

    [Fact]
    public void Array_property_is_registered_as_a_collection()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(MemberDto[] Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::TestNamespace.MemberDto[], global::TestNamespace.MemberDto>()");
    }

    [Fact]
    public void Dto_graph_without_a_value_object_emits_nothing()
    {
        const string source = """
            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(string Email);

                public sealed record TeamCommand(List<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGeneratorOrNull(source);

        generated.Should().BeNull("no property graph reaches a scalar value object, so there is nothing to wrap");
    }

    [Fact]
    public void Deeply_nested_containers_are_all_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record AddressDto(Email Email);

                public sealed record MemberDto(AddressDto Address);

                public sealed record TeamCommand(List<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
        generated.Should().Contain("RegisterObject<global::TestNamespace.AddressDto>()",
            "the walk must continue through the collection element to reach nested containers");
    }

    [Fact]
    public void Self_referential_graph_terminates()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Text.Json.Serialization;

                public sealed record NodeDto(Email Email, NodeDto? Child);

                [JsonSerializable(typeof(NodeDto))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterObject<global::TestNamespace.NodeDto>()");
    }

    [Fact]
    public void Unsupported_collection_shape_is_not_registered()
    {
        // Stack<T> is enumerable but List<T> is not assignable to it, so the runtime pipeline leaves it
        // to STJ. The generator must make the identical choice or AOT and reflection would diverge.
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(Stack<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGeneratorOrNull(source);

        (generated ?? string.Empty).Should().NotContain("RegisterCollection<global::System.Collections.Generic.Stack");
    }

    [Fact]
    public void Interface_collection_property_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(IReadOnlyList<MemberDto> Members);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.IReadOnlyList<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
    }

    [Fact]
    public void Direct_scalar_interface_collection_property_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record TeamCommand(IReadOnlyList<Email> Emails);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.IReadOnlyList<global::Email>, global::Email>()");
    }

    [Fact]
    public void Direct_scalar_dictionary_property_is_registered()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;
                using System.Text.Json.Serialization;

                public sealed record TeamCommand(Dictionary<string, Email> Prices);

                [JsonSerializable(typeof(TeamCommand))]
                public partial class AppContext : JsonSerializerContext { }
            }
            """;

        var generated = RunGenerator(source);

        generated.Should().Contain("RegisterDictionary<global::System.Collections.Generic.Dictionary<string, global::Email>, global::Email>()");
    }

    [Fact]
    public void Type_without_a_json_serializable_root_emits_nothing()
    {
        var source = $$"""
            {{ValueObject}}

            namespace TestNamespace
            {
                using System.Collections.Generic;

                public sealed record MemberDto(Email Email);

                public sealed record TeamCommand(List<MemberDto> Members);
            }
            """;

        var generated = RunGeneratorOrNull(source);

        generated.Should().BeNull("discovery starts from [JsonSerializable] roots");
    }

    private static string RunGenerator(string source)
    {
        var generated = RunGeneratorOrNull(source);
        generated.Should().NotBeNull("the generator should have emitted registrations for this graph");
        return generated!;
    }

    private static string CreateNestedConverterSource(
        string converterAccessibility, string constructorAccessibility, string parameters) => $$"""
        [global::System.Text.Json.Serialization.JsonConverter(typeof(Sample.Converter))]
        public class Sample : global::Trellis.ScalarValueObject<Sample, int>,
            global::Trellis.IScalarValue<Sample, int>
        {
            private Sample(int value) : base(value) { }
            public static global::Trellis.Result<Sample> TryCreate(int value, string? fieldName = null) =>
                global::Trellis.Result.Ok(new Sample(value));
            public static global::Trellis.Result<Sample> TryCreate(string? value, string? fieldName = null) =>
                global::Trellis.Result.Ok(new Sample(int.Parse(value!)));

            {{converterAccessibility}} sealed class Converter : global::Trellis.ResultJsonConverter<Sample>
            {
                {{constructorAccessibility}} Converter({{parameters}}) { }
                public override global::Trellis.Result<Sample> TryRead(
                    ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type type,
                    global::System.Text.Json.JsonSerializerOptions options) =>
                    Sample.TryCreate(reader.GetInt32());
                public override void Write(global::System.Text.Json.Utf8JsonWriter writer, Sample value,
                    global::System.Text.Json.JsonSerializerOptions options) =>
                    writer.WriteNumberValue(value.Value);
            }

            [global::System.Text.Json.Serialization.JsonSerializable(typeof(Sample))]
            public partial class AppContext : global::System.Text.Json.Serialization.JsonSerializerContext
            {
                public AppContext() : base(null) { }
                protected override global::System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(
                    global::System.Type type) => null;
            }
        }
        """;

    private static string? RunGeneratorOrNull(string source, bool checkCompilation = false)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName: "PathTrackingRegistryGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PathTrackingRegistryGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the source generator should not produce errors");
        if (checkCompilation)
            updatedCompilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty("generated registrations must preserve valid consumer compilation");

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Where(s => s.HintName.Contains("PathTracking"))
            .Select(s => s.SourceText.ToString())
            .FirstOrDefault();
    }

    private static MetadataReference[] GetMetadataReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Concat([
                typeof(RequiredGuid<>).Assembly.Location,
                typeof(ScalarValueObject<,>).Assembly.Location,
                typeof(Trellis.Primitives.CompositeValueObjectJsonConverter<>).Assembly.Location,
                typeof(Trellis.Asp.ValidationErrorsContext).Assembly.Location,
                typeof(System.Text.Json.JsonSerializer).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}