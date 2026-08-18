namespace Trellis.Primitives.Tests;

using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis.Testing;
using Xunit;

/// <summary>
/// Roslyn source generators all analyze the same original compilation and cannot observe
/// one another's output. The <c>[JsonConverter]</c> that Trellis emits onto a value object
/// is therefore invisible to System.Text.Json's own generator, which then treats the value
/// object as a POCO and emits <c>new SgOrderId()</c> — a constructor that does not exist.
/// Declaring the attribute in original source is the only way to inform STJ, so Trellis must
/// step aside rather than emit a duplicate.
/// </summary>
[JsonConverter(typeof(ParsableJsonConverter<SgOrderId>))]
public partial class SgOrderId : RequiredGuid<SgOrderId>
{
}

public sealed class SgOrder
{
    public SgOrderId? Id { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// The enum-backed emission site is a second, independent code path in the generator. It
/// carries the same duplicate-attribute hazard as the scalar site, so it needs its own real
/// compilation guarding it — a generator unit test cannot catch a cross-generator clash.
/// </summary>
[JsonConverter(typeof(RequiredEnumJsonConverter<SgFulfilment>))]
public partial class SgFulfilment : RequiredEnum<SgFulfilment>
{
    public static readonly SgFulfilment Pending = new();

    public static readonly SgFulfilment Shipped = new();
}

public sealed class SgShipment
{
    public SgFulfilment? Fulfilment { get; set; }
}

[JsonSerializable(typeof(SgOrder))]
[JsonSerializable(typeof(SgShipment))]
public partial class SgOrderJsonContext : JsonSerializerContext
{
}

public class SourceGeneratedJsonContextTests
{
    [Fact]
    public void ValueObject_reachable_from_source_generated_context_serializes_as_scalar()
    {
        var id = SgOrderId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var order = new SgOrder { Id = id, Description = "boxed set" };

        var json = JsonSerializer.Serialize(order, SgOrderJsonContext.Default.SgOrder);

        json.Should().Contain("\"11111111-1111-1111-1111-111111111111\"",
            "the user-declared [JsonConverter] must win, producing a scalar rather than a wrapped object");
        json.Should().NotContain("\"Value\"",
            "a wrapped {\"Value\":...} shape would mean STJ fell back to POCO metadata");
    }

    [Fact]
    public void ValueObject_reachable_from_source_generated_context_deserializes_from_scalar()
    {
        const string json = """{"Id":"22222222-2222-2222-2222-222222222222","Description":"boxed set"}""";

        var order = JsonSerializer.Deserialize(json, SgOrderJsonContext.Default.SgOrder);

        order.Should().NotBeNull();
        order!.Id.Should().NotBeNull();
        order.Id!.Value.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        order.Description.Should().Be("boxed set");
    }

    [Fact]
    public void SourceGeneratedContext_resolves_value_object_through_the_declared_converter()
    {
        var typeInfo = SgOrderJsonContext.Default.GetTypeInfo(typeof(SgOrderId));

        typeInfo.Should().NotBeNull();
        typeInfo!.Converter.Should().BeOfType<ParsableJsonConverter<SgOrderId>>(
            "STJ must route the value object through the converter named in original source");
    }

    [Fact]
    public void EnumValueObject_reachable_from_source_generated_context_round_trips_as_symbolic_name()
    {
        var shipment = new SgShipment { Fulfilment = SgFulfilment.Shipped };

        var json = JsonSerializer.Serialize(shipment, SgOrderJsonContext.Default.SgShipment);

        json.Should().Contain("\"Shipped\"",
            "the enum value object must serialize to its symbolic name, not POCO metadata");

        var restored = JsonSerializer.Deserialize(json, SgOrderJsonContext.Default.SgShipment);

        restored.Should().NotBeNull();
        restored!.Fulfilment.Should().Be(SgFulfilment.Shipped);
    }

    [Fact]
    public void SourceGeneratedContext_resolves_enum_value_object_through_the_declared_converter()
    {
        var typeInfo = SgOrderJsonContext.Default.GetTypeInfo(typeof(SgFulfilment));

        typeInfo.Should().NotBeNull();
        typeInfo!.Converter.Should().BeOfType<RequiredEnumJsonConverter<SgFulfilment>>(
            "the enum emission site must also step aside for the user-declared converter");
    }
}