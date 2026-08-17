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

        generated.Should().Contain("RegisterCollection<global::System.Collections.Generic.List<global::TestNamespace.MemberDto>, global::TestNamespace.MemberDto>()");
        generated.Should().Contain("[ModuleInitializer]");
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

    private static string? RunGeneratorOrNull(string source)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName: "PathTrackingRegistryGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PathTrackingRegistryGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, cancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the source generator should not produce errors");

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
                typeof(Trellis.Asp.ValidationErrorsContext).Assembly.Location,
                typeof(System.Text.Json.JsonSerializer).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();
}
