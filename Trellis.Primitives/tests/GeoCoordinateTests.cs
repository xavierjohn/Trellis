namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis.Testing;

public class GeoCoordinateTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(90, -180)]
    [InlineData(-90, 180)]
    [InlineData(47.6062095, -122.3320708)]
    [InlineData(-33.8688, 151.2093)]
    public void TryCreate_ValidCoordinates_PreservesComponents(double latitude, double longitude)
    {
        var coordinate = GeoCoordinate.TryCreate(latitude, longitude).Should().BeSuccess().Which;

        coordinate.Latitude.Should().Be(latitude);
        coordinate.Longitude.Should().Be(longitude);
    }

    [Theory]
    [InlineData(-90.000000001, 0, "/latitude", ValidationCodes.ValueGreaterThanOrEqual, -90)]
    [InlineData(90.000000001, 0, "/latitude", ValidationCodes.ValueLessThanOrEqual, 90)]
    [InlineData(0, -180.000000001, "/longitude", ValidationCodes.ValueGreaterThanOrEqual, -180)]
    [InlineData(0, 180.000000001, "/longitude", ValidationCodes.ValueLessThanOrEqual, 180)]
    [InlineData(double.MinValue, 0, "/latitude", ValidationCodes.ValueGreaterThanOrEqual, -90)]
    [InlineData(0, double.MaxValue, "/longitude", ValidationCodes.ValueLessThanOrEqual, 180)]
    public void TryCreate_OutOfRange_ReportsDirectionalBound(
        double latitude, double longitude, string path, string reason, int bound)
    {
        var error = GeoCoordinate.TryCreate(latitude, longitude)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Length.Should().Be(1);
        var violation = error.Fields[0];
        violation.Field.Path.Should().Be(path);
        violation.ReasonCode.Should().Be(reason);
        violation.Args.Should().BeEquivalentTo(ValidationArgs.Of("comparisonValue", bound));
    }

    [Theory]
    [InlineData(double.NaN, 0, "/latitude")]
    [InlineData(double.PositiveInfinity, 0, "/latitude")]
    [InlineData(double.NegativeInfinity, 0, "/latitude")]
    [InlineData(0, double.NaN, "/longitude")]
    [InlineData(0, double.PositiveInfinity, "/longitude")]
    [InlineData(0, double.NegativeInfinity, "/longitude")]
    public void TryCreate_NonFinite_ReportsNumericValidation(double latitude, double longitude, string path)
    {
        var error = GeoCoordinate.TryCreate(latitude, longitude)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Field.Path.Should().Be(path);
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.NumberFinite);
        error.Fields[0].Args.Should().BeNull();
    }

    [Theory]
    [InlineData(91, -181)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    public void TryCreate_BothComponentsInvalid_AccumulatesViolations(double latitude, double longitude)
    {
        var error = GeoCoordinate.TryCreate(latitude, longitude)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/latitude", "/longitude"]);
        error.Fields.Items.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Detail));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Location", "/location")]
    [InlineData("/items/0/location", "/items/0/location")]
    [InlineData("location/name", "/location~1name")]
    [InlineData("location~name", "/location~0name")]
    [InlineData("/items/0/a~1b", "/items/0/a~1b")]
    public void TryCreate_CustomOwner_ComposesComponentPointers(string? fieldName, string parent)
    {
        var error = GeoCoordinate.TryCreate(91, 181, fieldName)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path)
            .Should().Equal([$"{parent}/latitude", $"{parent}/longitude"]);
    }

    [Fact]
    public void Create_ValidCoordinates_ReturnsCoordinate()
    {
        var coordinate = GeoCoordinate.Create(12.5, -34.75);

        coordinate.Latitude.Should().Be(12.5);
        coordinate.Longitude.Should().Be(-34.75);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public void Create_InvalidCoordinates_Throws(double latitude, double longitude)
    {
        Action act = () => GeoCoordinate.Create(latitude, longitude);

        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to create GeoCoordinate:*");
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 111195.0802335329)]
    [InlineData(0, 0, 90, 0, 10007557.221017962)]
    [InlineData(51.5074, -0.1278, 40.7128, -74.006, 5570229.873656523)]
    [InlineData(36.12, -86.67, 33.94, -118.4, 2886448.429764855)]
    [InlineData(89, 0, 89, 180, 222390.16046706692)]
    [InlineData(-89, 0, -89, 180, 222390.16046706692)]
    [InlineData(0, 179.9, 0, -179.9, 22239.016046706758)]
    [InlineData(0, 0, 0.000001, 0, 0.1111950802335329)]
    public void DistanceMetersTo_KnownSphericalArc_ReturnsMetersSymmetrically(
        double latitude, double longitude, double otherLatitude, double otherLongitude, double expected)
    {
        var origin = GeoCoordinate.Create(latitude, longitude);
        var destination = GeoCoordinate.Create(otherLatitude, otherLongitude);

        origin.DistanceMetersTo(destination).Should().BeApproximately(expected, 0.001);
        destination.DistanceMetersTo(origin).Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData(47.6, -122.3, 47.6, -122.3)]
    [InlineData(0, 180, 0, -180)]
    [InlineData(45, 180, 45, -180)]
    [InlineData(90, -100, 90, 75)]
    [InlineData(-90, 20, -90, 150)]
    public void DistanceMetersTo_SamePhysicalPoint_ReturnsZero(
        double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        var origin = GeoCoordinate.Create(latitude, longitude);
        var destination = GeoCoordinate.Create(otherLatitude, otherLongitude);

        origin.DistanceMetersTo(destination).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0, 0, 180)]
    [InlineData(90, 0, -90, 0)]
    [InlineData(36.12, -86.67, -36.12, 93.33)]
    [InlineData(36.12, -86.67, -36.1200001, 93.3299999)]
    public void DistanceMetersTo_AntipodalOrNearlyAntipodal_IsFiniteAndBounded(
        double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        const double halfCircumference = 20015114.442035925;
        var origin = GeoCoordinate.Create(latitude, longitude);
        var destination = GeoCoordinate.Create(otherLatitude, otherLongitude);

        var distance = origin.DistanceMetersTo(destination);

        double.IsFinite(distance).Should().BeTrue();
        distance.Should().BeLessThanOrEqualTo(halfCircumference);
        distance.Should().BeApproximately(halfCircumference, 0.5);
    }

    [Fact]
    public void DistanceMetersTo_NullDestination_Throws()
    {
        var origin = GeoCoordinate.Create(0, 0);

        Action act = () => origin.DistanceMetersTo(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("other");
    }

    [Fact]
    public void Equals_IdenticalComponents_UsesInheritedEqualityAndHashing()
    {
        var first = GeoCoordinate.Create(47.6, -122.3);
        var second = GeoCoordinate.Create(47.6, -122.3);

        first.Equals(second).Should().BeTrue();
        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.CompareTo(second).Should().Be(0);
    }

    [Theory]
    [InlineData(10, 20, 11, 20)]
    [InlineData(10, 20, 10, 21)]
    [InlineData(0, -180, 0, 180)]
    [InlineData(90, 0, 90, 10)]
    public void Equals_DifferentComponents_DoesNotNormalizeOrUseDistance(
        double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        var first = GeoCoordinate.Create(latitude, longitude);
        var second = GeoCoordinate.Create(otherLatitude, otherLongitude);

        (first == second).Should().BeFalse();
        first.CompareTo(second).Should().BeLessThan(0);
    }

    [Fact]
    public void Json_ValidCoordinate_RoundTripsNumericObject()
    {
        var coordinate = GeoCoordinate.Create(47.6062, -122.3321);

        var json = JsonSerializer.Serialize(coordinate);

        json.Should().Be("""{"latitude":47.6062,"longitude":-122.3321}""");
        JsonSerializer.Deserialize<GeoCoordinate>(json).Should().Be(coordinate);
    }

    [Fact]
    public void Json_SourceGeneratedContext_RoundTrips()
    {
        var coordinate = GeoCoordinate.Create(-33.8688, 151.2093);
        var typeInfo = GeoCoordinateJsonContext.Default.GeoCoordinate;

        var json = JsonSerializer.Serialize(coordinate, typeInfo);

        JsonSerializer.Deserialize(json, typeInfo).Should().Be(coordinate);
    }

    [Fact]
    public void Json_InvalidComponents_PreservesAllStructuredViolations()
    {
        Action act = () => JsonSerializer.Deserialize<GeoCoordinate>("""{"latitude":91,"longitude":-181}""");

        var exception = act.Should().Throw<TrellisJsonValidationException>().Which;
        var error = Assert.IsType<Error.InvalidInput>(exception.InvalidInput);
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/latitude", "/longitude"]);
        error.Fields.Items.Select(v => v.ReasonCode)
            .Should().Equal([ValidationCodes.ValueLessThanOrEqual, ValidationCodes.ValueGreaterThanOrEqual]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"latitude":0}""")]
    [InlineData("""{"longitude":0}""")]
    [InlineData("""{"latitude":null,"longitude":0}""")]
    [InlineData("""{"latitude":0,"longitude":null}""")]
    [InlineData("""{"latitude":"not-a-number","longitude":0}""")]
    [InlineData("""{"latitude":1e400,"longitude":0}""")]
    [InlineData("[]")]
    public void Json_MissingOrInvalidComponent_RejectsInsteadOfUsingZero(string json)
    {
        Action act = () => JsonSerializer.Deserialize<GeoCoordinate>(json);

        act.Should().Throw<TrellisJsonValidationException>();
    }

    [Fact]
    public void ToString_NonEnglishCulture_UsesInvariantComponents()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            GeoCoordinate.Create(47.5, -122.25).ToString().Should().Be("(47.5, -122.25)");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}

[JsonSerializable(typeof(GeoCoordinate))]
internal partial class GeoCoordinateJsonContext : JsonSerializerContext;
