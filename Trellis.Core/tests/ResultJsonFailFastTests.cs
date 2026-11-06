namespace Trellis.Core.Tests;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

/// <summary>
/// Pins the fail-fast behavior of <see cref="ResultRequiresExplicitHttpMappingConverter"/>:
/// direct JSON serialization / deserialization of <see cref="Result{TValue}"/> (or an
/// <see cref="IResult"/> / <see cref="IResult{TValue}"/>-declared receiver) throws an
/// actionable <see cref="NotSupportedException"/> instead of silently producing a struct
/// dump. The intended path is <c>.ToHttpResponse()</c> (Trellis.Asp) — proven by checking
/// the message wording — and consumers who legitimately want raw JSON of a Result can
/// override via <c>JsonSerializerOptions.Converters</c>. The override must match the
/// declared static type; the per-shape rule and the <see cref="System.Text.Json.Serialization.JsonConverterFactory"/>
/// alternative are pinned in dedicated tests below.
/// </summary>
public class ResultJsonFailFastTests
{
    [Fact]
    public void Serialize_Result_throws_with_actionable_message()
    {
        var result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_Result_throws_with_actionable_message()
    {
        var act = () => JsonSerializer.Deserialize<Result<int>>("{\"IsSuccess\":true,\"Value\":42}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_failure_Result_also_throws_with_actionable_message()
    {
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Order", "42")));

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized*Match*TryGetValue*");
    }

    [Fact]
    public void Consumer_override_in_options_takes_precedence_over_default_throwing_converter()
    {
        // Legitimate escape hatch: consumer who really wants to serialize Result<T> (logging,
        // IPC, storage) registers a JsonConverter<Result<T>> in JsonSerializerOptions.Converters.
        // STJ's converter resolution preferences the option-registered converter over the
        // type's [JsonConverter] attribute, so the throwing default is bypassed.
        var result = Result.Ok(42);

        var json = JsonSerializer.Serialize(result, s_optionsWithRawResultConverter);

        json.Should().Be("\"OK:42\"");
    }

    [Fact]
    public void Serializing_a_DTO_containing_Result_property_also_throws()
    {
        // The default object formatter recursively serializes properties — the throw fires on
        // the nested Result<T> property exactly the same as a top-level Result<T>.
        var dto = new DtoCarryingResult(Result.Ok(42));

        var act = () => JsonSerializer.Serialize(dto);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*ToHttpResponse*");
    }

    // ---------- Interface-declared return-type coverage ----------

    [Fact]
    public void Serialize_via_IResult_T_interface_also_throws()
    {
        // STJ resolves [JsonConverter] against the static declared type. A controller signature
        // like Task<IResult<int>> GetAsync() would previously bypass the throwing converter
        // because the attribute was only on the struct, not the interface — re-creating the
        // exact silent-struct-dump bug this PR is meant to prevent. The attribute now lives on
        // both the struct AND the interfaces to close the gap. The exception message names
        // the *declared* shape so the consumer sees IResult<Int32>, not Result<Int32>.
        IResult<int> result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_DTO_with_IResult_T_property_also_throws()
    {
        var dto = new DtoCarryingIResultT(Result.Ok(42));

        var act = () => JsonSerializer.Serialize(dto);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_via_non_generic_IResult_also_throws()
    {
        IResult result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult*ToHttpResponse*");
    }

    // ---------- Override path is per-declared-shape ----------

    [Fact]
    public void JsonConverter_for_Result_T_does_not_override_IResult_T_declaration()
    {
        // The override path is per-declared-static-type. A JsonConverter<Result<int>> in
        // options.Converters does NOT match an IResult<int>-declared value, so the interface
        // attribute still fires and throws. This is the trap the exception message warns
        // about and that the docs now spell out.
        IResult<int> result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result, s_optionsWithRawResultConverter);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*");
    }

    [Fact]
    public void Consumer_can_override_IResult_T_via_options_registered_converter()
    {
        // Correct shape: register a converter typed for the declared shape.
        IResult<int> result = Result.Ok(42);

        var json = JsonSerializer.Serialize(result, s_optionsWithRawIResultTConverter);

        json.Should().Be("\"IResult:OK\"");
    }

    [Fact]
    public void Consumer_can_override_all_result_shapes_with_a_factory()
    {
        // Mixed-shape override: a single JsonConverterFactory whose CanConvert covers every
        // result shape lets the consumer route all of them through one custom path.
        IResult<int> ir = Result.Ok(42);
        var concrete = Result.Ok(99);

        JsonSerializer.Serialize(ir, s_optionsWithFactoryOverride).Should().Be("\"any:result\"");
        JsonSerializer.Serialize(concrete, s_optionsWithFactoryOverride).Should().Be("\"any:result\"");
    }

    // ---------- Deserialization through every declared shape ----------

    [Fact]
    public void Deserialize_via_IResult_T_interface_throws_naming_the_declared_shape()
    {
        var act = () => JsonSerializer.Deserialize<IResult<int>>("{\"IsSuccess\":true}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("IResult<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_via_non_generic_IResult_throws_naming_the_declared_shape()
    {
        var act = () => JsonSerializer.Deserialize<IResult>("{\"IsSuccess\":true}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("IResult cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_DTO_with_a_Result_property_throws_at_converter_creation()
    {
        // The throw comes from creating the converter for the declared type, which happens in both
        // directions, so the JSON payload is irrelevant.
        var act = () => JsonSerializer.Deserialize<DtoCarryingResult>("{\"Inner\":null}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_DTO_with_an_IResult_T_property_throws_at_converter_creation()
    {
        // The throw comes from creating the converter for the declared type, which happens in both
        // directions, so the JSON payload is irrelevant.
        var act = () => JsonSerializer.Deserialize<DtoCarryingIResultT>("{\"Inner\":null}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("IResult<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    // ---------- Nested declared types ----------

    [Fact]
    public void Serialize_list_of_Result_throws()
    {
        List<Result<int>> results = [Result.Ok(1), Result.Ok(2)];

        var act = () => JsonSerializer.Serialize(results);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_array_of_IResult_T_throws()
    {
        IResult<int>[] results = [Result.Ok(1)];

        var act = () => JsonSerializer.Serialize(results);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("IResult<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_dictionary_with_Result_values_throws()
    {
        var results = new Dictionary<string, Result<int>> { ["a"] = Result.Ok(1) };

        var act = () => JsonSerializer.Serialize(results);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_list_of_Result_throws_at_converter_creation()
    {
        // The throw comes from creating the converter for the declared type, which happens in both
        // directions, so the JSON payload is irrelevant.
        var act = () => JsonSerializer.Deserialize<List<Result<int>>>("[]");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_nullable_Result_throws_at_converter_creation()
    {
        // Nullable<Result<int>> wraps the Result<int> converter, so the factory throws while building it,
        // before the null value is inspected.
        Result<int>? absent = null;

        var act = () => JsonSerializer.Serialize(absent);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_boxed_Result_through_an_object_declaration_throws_for_the_runtime_type()
    {
        // A value declared as object is serialized with the runtime type's converter, which is the
        // [JsonConverter] attribute on Result<T>, so boxing does not bypass the guard.
        object boxed = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(boxed);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Result<Int32> cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    // ---------- Factory contract ----------

    [Theory]
    [InlineData(typeof(Result<int>), true)]
    [InlineData(typeof(Result<string>), true)]
    [InlineData(typeof(IResult<int>), true)]
    [InlineData(typeof(IResult<Order>), true)]
    [InlineData(typeof(IResult), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(Maybe<int>), false)]
    [InlineData(typeof(List<Result<int>>), false)]
    [InlineData(typeof(Result<int>[]), false)]
    [InlineData(typeof(Result<int>?), false)]
    [InlineData(typeof(Unit), false)]
    public void CanConvert_matches_exactly_the_result_shapes(Type type, bool expected)
    {
        var factory = new ResultRequiresExplicitHttpMappingConverter();

        factory.CanConvert(type).Should().Be(expected);
    }

    [Fact]
    public void CanConvert_rejects_a_null_type()
    {
        var factory = new ResultRequiresExplicitHttpMappingConverter();

        var act = () => factory.CanConvert(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("typeToConvert");
    }

    [Fact]
    public void CreateConverter_rejects_a_null_type()
    {
        var factory = new ResultRequiresExplicitHttpMappingConverter();

        var act = () => factory.CreateConverter(null!, new JsonSerializerOptions());

        act.Should().Throw<ArgumentNullException>().WithParameterName("typeToConvert");
    }

    [Theory]
    [InlineData(typeof(Result<int>), "Result<Int32>")]
    [InlineData(typeof(Result<string>), "Result<String>")]
    [InlineData(typeof(IResult<int>), "IResult<Int32>")]
    [InlineData(typeof(IResult), "IResult")]
    public void CreateConverter_always_throws_and_names_the_declared_shape(Type type, string shape)
    {
        var factory = new ResultRequiresExplicitHttpMappingConverter();

        var act = () => factory.CreateConverter(type, new JsonSerializerOptions());

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"{shape} cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    private sealed record Order(int Id);

    private sealed record DtoCarryingResult(Result<int> Inner);

    private sealed record DtoCarryingIResultT(IResult<int> Inner);

    private static readonly JsonSerializerOptions s_optionsWithRawResultConverter = new()
    {
        Converters = { new RawResultConverter() },
    };

    private static readonly JsonSerializerOptions s_optionsWithRawIResultTConverter = new()
    {
        Converters = { new RawIResultTConverter() },
    };

    private static readonly JsonSerializerOptions s_optionsWithFactoryOverride = new()
    {
        Converters = { new AllResultShapesFactory() },
    };

    private sealed class RawResultConverter : JsonConverter<Result<int>>
    {
        public override Result<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotImplementedException();

        public override void Write(Utf8JsonWriter writer, Result<int> value, JsonSerializerOptions options)
        {
            value.TryGetValue(out var v);
            writer.WriteStringValue($"OK:{v}");
        }
    }

    private sealed class RawIResultTConverter : JsonConverter<IResult<int>>
    {
        public override IResult<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotImplementedException();

        public override void Write(Utf8JsonWriter writer, IResult<int> value, JsonSerializerOptions options) =>
            writer.WriteStringValue("IResult:OK");
    }

    private sealed class AllResultShapesFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert == typeof(IResult)) return true;
            if (!typeToConvert.IsGenericType) return false;
            var def = typeToConvert.GetGenericTypeDefinition();
            return def == typeof(Result<>) || def == typeof(IResult<>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new ConstantStringConverter();

        private sealed class ConstantStringConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotImplementedException();

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
                writer.WriteStringValue("any:result");

            public override bool CanConvert(Type typeToConvert) => true;
        }
    }
}