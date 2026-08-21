namespace Trellis.Primitives.Tests;

using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Pins that every read-path rejection in <see cref="CompositeValueObjectJsonConverter{T}"/>
/// carries a structured, composite-relative violation.
/// </summary>
/// <remarks>
/// These throws previously carried a message and nothing else, so the boundary had a curated
/// English sentence and no pointer to attach it to. The pointers here are relative to the value
/// object because the converter does not know where in the request document it was invoked; the
/// caller re-roots them.
/// </remarks>
public sealed class CompositeValueObjectJsonConverterViolationTests
{
    private static TrellisJsonValidationException Deserialize(string json) =>
        Assert.Throws<TrellisJsonValidationException>(
            () => JsonSerializer.Deserialize<ViolationComposite>(json));

    [Fact]
    public void A_non_object_token_reports_a_root_relative_violation()
    {
        var ex = Deserialize("42");

        var violation = ex.InvalidInput!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().BeEmpty("the whole value, not one of its properties, is wrong");
        violation.Detail.Should().Be(ex.Message);
    }

    [Fact]
    public void Missing_properties_report_one_violation_each_not_one_naming_several()
    {
        var ex = Deserialize("""{"name": "n"}""");

        ex.InvalidInput!.Fields.Items.Select(f => f.Field.Path)
            .Should().BeEquivalentTo(["/count", "/label"]);

        ex.InvalidInput.Detail.Should().Contain("'count'").And.Contain("'label'",
            "the joined sentence is retained at the error level so existing rendering is unchanged");
    }

    [Fact]
    public void A_string_property_present_as_null_reports_at_that_property()
    {
        var ex = Deserialize("""{"name": null, "count": 1, "label": "l"}""");

        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/name");
    }

    [Fact]
    public void A_numeric_property_present_as_null_reports_at_that_property()
    {
        var ex = Deserialize("""{"name": "n", "count": null, "label": "l"}""");

        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/count");
    }

    [Fact]
    public void A_wrong_token_type_reports_at_that_property()
    {
        var ex = Deserialize("""{"name": "n", "count": "not-a-number", "label": "l"}""");

        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/count");
    }

    [Fact]
    public void A_TryCreate_failure_still_surfaces_its_own_structured_error()
    {
        var ex = Deserialize("""{"name": "", "count": 1, "label": "l"}""");

        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/name");
    }

    public sealed record ViolationBox(ViolationComposite Value);
}

[JsonConverter(typeof(CompositeValueObjectJsonConverter<ViolationComposite>))]
public sealed class ViolationComposite : ValueObject
{
    public string Name { get; private set; } = string.Empty;

    public int Count { get; private set; }

    public string Label { get; private set; } = string.Empty;

    private ViolationComposite() { }

    private ViolationComposite(string name, int count, string label)
    {
        Name = name;
        Count = count;
        Label = label;
    }

    public static Result<ViolationComposite> TryCreate(string name, int count, string label, string? fieldName = null) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Fail<ViolationComposite>(
                Error.InvalidInput.ForField(InputPointer.ForProperty("name"), ValidationCodes.Unspecified, "Name is required."))
            : Result.Ok(new ViolationComposite(name, count, label));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Name);
        components.Add(Count);
        components.Add(Label);
    }
}