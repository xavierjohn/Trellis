namespace Trellis.Primitives.Tests;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;
using Trellis.Testing;
using Xunit;

// --- Inner scalar value objects: both bare lenient under the new defaults ---

public partial class FlowThroughLenientName : RequiredString<FlowThroughLenientName> { }

public partial class FlowThroughStrictName : RequiredString<FlowThroughStrictName> { }

// --- Composite VO whose inner scalar is lenient ---

[JsonConverter(typeof(CompositeValueObjectJsonConverter<FlowThroughLenientComposite>))]
public sealed class FlowThroughLenientComposite : ValueObject
{
    public FlowThroughLenientName Name { get; private set; } = null!;
    public int Quantity { get; private set; }

    private FlowThroughLenientComposite() { }
    private FlowThroughLenientComposite(FlowThroughLenientName name, int quantity)
    {
        Name = name;
        Quantity = quantity;
    }

    public static Result<FlowThroughLenientComposite> TryCreate(string name, int quantity, string? fieldName = null) =>
        FlowThroughLenientName.TryCreate(name, "name")
            .Map(n => new FlowThroughLenientComposite(n, quantity));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Name.Value);
        components.Add(Quantity);
    }
}

// --- Composite VO whose inner scalar is strict ---

[JsonConverter(typeof(CompositeValueObjectJsonConverter<FlowThroughStrictComposite>))]
public sealed class FlowThroughStrictComposite : ValueObject
{
    public FlowThroughStrictName Name { get; private set; } = null!;
    public int Quantity { get; private set; }

    private FlowThroughStrictComposite() { }
    private FlowThroughStrictComposite(FlowThroughStrictName name, int quantity)
    {
        Name = name;
        Quantity = quantity;
    }

    public static Result<FlowThroughStrictComposite> TryCreate(string name, int quantity, string? fieldName = null) =>
        FlowThroughStrictName.TryCreate(name, "name")
            .Map(n => new FlowThroughStrictComposite(n, quantity));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Name.Value);
        components.Add(Quantity);
    }
}

/// <summary>
/// Cross-cutting regression coverage: when a <see cref="CompositeValueObject{T}"/>-shaped
/// type contains a <see cref="RequiredString{TSelf}"/> field, the composite's <c>TryCreate</c>
/// delegates to the inner <c>TryCreate</c> — so the inner type's validation flows through.
/// Inner types under the lenient default accept <c>""</c>; types decorated with
/// <c>[NotDefault]</c> opt back into per-type sentinel rejection.
/// </summary>
/// <remarks>
/// This is the composite-VO mirror of the EF rehydration test
/// (<see cref="RequiredXxxRehydrationLenienceTests"/> in Trellis.EntityFrameworkCore.Tests):
/// both prove that the "inner type decides" rule holds at every layer that invokes
/// <c>TryCreate</c> on the inner scalar.
/// </remarks>
public class CompositeRequiredStringFlowThroughTests
{
    // ---- Direct TryCreate flow-through ----

    [Fact]
    public void Lenient_composite_TryCreate_accepts_empty_inner_string()
    {
        var result = FlowThroughLenientComposite.TryCreate("", 5);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Value.Should().Be("");
    }

    [Fact]
    public void Strict_composite_TryCreate_accepts_empty_inner_string()
    {
        // Lenient default — empty inner string is accepted by the bare RequiredString base.
        var result = FlowThroughStrictComposite.TryCreate("", 5);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Value.Should().Be("");
    }

    // ---- JSON deserialize flow-through ----

    [Fact]
    public void Lenient_composite_JSON_deserialize_accepts_empty_inner_string()
    {
        var json = "{\"name\":\"\",\"quantity\":5}";
        var vo = JsonSerializer.Deserialize<FlowThroughLenientComposite>(json);
        vo.Should().NotBeNull();
        vo!.Name.Value.Should().Be("");
        vo.Quantity.Should().Be(5);
    }

    [Fact]
    public void Strict_composite_JSON_deserialize_accepts_empty_inner_string()
    {
        // Lenient default — empty inner string deserializes successfully.
        var json = "{\"name\":\"\",\"quantity\":5}";
        var vo = JsonSerializer.Deserialize<FlowThroughStrictComposite>(json);
        vo.Should().NotBeNull();
        vo!.Name.Value.Should().Be("");
        vo.Quantity.Should().Be(5);
    }

    // ---- Non-sentinel happy paths to prove the composite still works ----

    [Fact]
    public void Lenient_composite_accepts_normal_string() =>
        FlowThroughLenientComposite.TryCreate("Alice", 5).IsSuccess.Should().BeTrue();

    [Fact]
    public void Strict_composite_accepts_normal_string() =>
        FlowThroughStrictComposite.TryCreate("Alice", 5).IsSuccess.Should().BeTrue();
}