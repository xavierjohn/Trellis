namespace Trellis.Primitives.Tests;

using Trellis.Testing;

public class GeoBoundsTests
{
    private const double MeanEarthRadiusMeters = 6_371_008.8;

    [Theory]
    [InlineData(double.NaN, ValidationCodes.NumberFinite)]
    [InlineData(double.PositiveInfinity, ValidationCodes.NumberFinite)]
    [InlineData(double.NegativeInfinity, ValidationCodes.NumberFinite)]
    [InlineData(-0.001, ValidationCodes.ValueGreaterThanOrEqual)]
    public void TryCreate_InvalidRadius_ReturnsFieldViolation(double radiusMeters, string reasonCode)
    {
        var error = GeoBounds.TryCreate(GeoCoordinate.Create(0, 0), radiusMeters)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Should().ContainSingle();
        error.Fields[0].Field.Path.Should().Be("/radiusMeters");
        error.Fields[0].ReasonCode.Should().Be(reasonCode);
        if (reasonCode == ValidationCodes.ValueGreaterThanOrEqual)
            error.Fields[0].Args.Should().BeEquivalentTo(ValidationArgs.Of("comparisonValue", 0));
        else
            error.Fields[0].Args.Should().BeNull();
    }

    [Fact]
    public void TryCreate_CustomRadiusField_UsesConfiguredPointer()
    {
        var error = GeoBounds.TryCreate(GeoCoordinate.Create(0, 0), -1, "/query/radius")
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/query/radius");
    }

    [Fact]
    public void TryCreate_NullCenter_Throws()
    {
        var act = () => GeoBounds.TryCreate(null!, 1);

        act.Should().Throw<ArgumentNullException>().WithParameterName("center");
    }

    [Fact]
    public void Create_InvalidRadius_Throws()
    {
        var act = () => GeoBounds.Create(GeoCoordinate.Create(0, 0), -1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryCreate_OrdinaryRadius_ReturnsSingleExpandedBox()
    {
        var radius = MeanEarthRadiusMeters * Math.PI / 180;

        var bounds = GeoBounds.TryCreate(GeoCoordinate.Create(0, 0), radius)
            .Should().BeSuccess().Which;

        bounds.Center.Should().Be(GeoCoordinate.Create(0, 0));
        bounds.RadiusMeters.Should().Be(radius);
        bounds.Boxes.Should().ContainSingle();
        var box = bounds.Boxes[0];
        box.MinimumLatitude.Should().BeApproximately(-1, 1e-9);
        box.MaximumLatitude.Should().BeApproximately(1, 1e-9);
        box.MinimumLongitude.Should().BeApproximately(-1, 1e-9);
        box.MaximumLongitude.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void TryCreate_AntimeridianCrossing_ReturnsTwoNonWrappingBoxes()
    {
        var radius = MeanEarthRadiusMeters * Math.PI / 180;

        var bounds = GeoBounds.Create(GeoCoordinate.Create(0, 179.5), radius);

        bounds.Boxes.Should().HaveCount(2);
        bounds.Boxes[0].MinimumLongitude.Should().Be(-180);
        bounds.Boxes[0].MaximumLongitude.Should().BeApproximately(-179.5, 1e-9);
        bounds.Boxes[1].MinimumLongitude.Should().BeApproximately(178.5, 1e-9);
        bounds.Boxes[1].MaximumLongitude.Should().Be(180);
        bounds.Boxes.Should().OnlyContain(box => box.MinimumLongitude <= box.MaximumLongitude);
        bounds.Contains(GeoCoordinate.Create(0, -179.75)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(0, 179.75)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(0, 177)).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_WestwardAntimeridianCrossing_ReturnsTwoNonWrappingBoxes()
    {
        var radius = MeanEarthRadiusMeters * Math.PI / 180;

        var bounds = GeoBounds.Create(GeoCoordinate.Create(0, -179.5), radius);

        bounds.Boxes.Should().HaveCount(2);
        bounds.Boxes[0].MinimumLongitude.Should().Be(-180);
        bounds.Boxes[0].MaximumLongitude.Should().BeApproximately(-178.5, 1e-9);
        bounds.Boxes[1].MinimumLongitude.Should().BeApproximately(179.5, 1e-9);
        bounds.Boxes[1].MaximumLongitude.Should().Be(180);
        bounds.Boxes.Should().OnlyContain(box => box.MinimumLongitude <= box.MaximumLongitude);
        bounds.Contains(GeoCoordinate.Create(0, -179.75)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(0, 179.75)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(0, -177)).Should().BeFalse();
    }

    [Theory]
    [InlineData(89.5, 90)]
    [InlineData(-89.5, -90)]
    public void TryCreate_RadiusReachingPole_CoversEveryLongitude(double latitude, double expectedExtreme)
    {
        var radius = MeanEarthRadiusMeters * Math.PI / 180;

        var bounds = GeoBounds.Create(GeoCoordinate.Create(latitude, 40), radius);

        bounds.Boxes.Should().ContainSingle();
        var box = bounds.Boxes[0];
        box.MinimumLongitude.Should().Be(-180);
        box.MaximumLongitude.Should().Be(180);
        (expectedExtreme > 0 ? box.MaximumLatitude : box.MinimumLatitude).Should().Be(expectedExtreme);
        bounds.Contains(GeoCoordinate.Create(expectedExtreme, -170)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(expectedExtreme, 170)).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void TryCreate_RadiusAtLeastHalfCircumference_CoversWorld(double multiplier)
    {
        var bounds = GeoBounds.Create(
            GeoCoordinate.Create(47.6062, -122.3321),
            Math.PI * MeanEarthRadiusMeters * multiplier);

        bounds.Boxes.Should().ContainSingle();
        bounds.Boxes[0].Should().Be(GeoBoundingBox.World);
        bounds.Contains(GeoCoordinate.Create(-90, -180)).Should().BeTrue();
        bounds.Contains(GeoCoordinate.Create(90, 180)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 180, 0, -180)]
    [InlineData(0, -180, 0, 180)]
    [InlineData(90, 0, 90, 179)]
    [InlineData(-90, 0, -90, -179)]
    public void TryCreate_ZeroRadius_IncludesPhysicallyEquivalentCoordinate(
        double latitude, double longitude, double equivalentLatitude, double equivalentLongitude)
    {
        var bounds = GeoBounds.Create(GeoCoordinate.Create(latitude, longitude), 0);

        bounds.Contains(GeoCoordinate.Create(equivalentLatitude, equivalentLongitude)).Should().BeTrue();
    }

    [Fact]
    public void Contains_NullCoordinate_Throws()
    {
        var bounds = GeoBounds.Create(GeoCoordinate.Create(0, 0), 100);

        var act = () => bounds.Contains(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("coordinate");
    }

    [Theory]
    [InlineData(-18.124535534588873, -90.74786923394905, -0.9538646465883858, -90.74786923394905)]
    [InlineData(0, 179.8, 0, -179.8)]
    [InlineData(89.5, 45, 89.9, -135)]
    [InlineData(89.5, 0, -89.9, 0)]
    public void Create_RadiusMeasuredToExistingCoordinate_ContainsBoundaryCoordinate(
        double originLatitude,
        double originLongitude,
        double targetLatitude,
        double targetLongitude)
    {
        var origin = GeoCoordinate.Create(originLatitude, originLongitude);
        var target = GeoCoordinate.Create(targetLatitude, targetLongitude);
        var radius = origin.DistanceMetersTo(target);

        var bounds = GeoBounds.Create(origin, radius);

        bounds.Contains(target).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 100_000)]
    [InlineData(0, 179.8, 100_000)]
    [InlineData(89.5, 45, 100_000)]
    [InlineData(-89.5, -135, 100_000)]
    [InlineData(45, -122, 2_000_000)]
    public void Create_BoundaryDestinations_NeverExcludesPointWithinRadius(
        double latitude, double longitude, double radiusMeters)
    {
        var center = GeoCoordinate.Create(latitude, longitude);
        var bounds = GeoBounds.Create(center, radiusMeters);

        for (var bearing = 0; bearing < 360; bearing += 5)
        {
            var destination = Destination(center, radiusMeters, bearing);
            if (center.DistanceMetersTo(destination) <= radiusMeters)
                bounds.Contains(destination).Should().BeTrue(
                    $"bearing {bearing}° is within {radiusMeters} m of {center}");
        }
    }

    private static GeoCoordinate Destination(GeoCoordinate center, double distanceMeters, double bearingDegrees)
    {
        var angularDistance = distanceMeters / MeanEarthRadiusMeters;
        var latitude = center.Latitude * Math.PI / 180;
        var longitude = center.Longitude * Math.PI / 180;
        var bearing = bearingDegrees * Math.PI / 180;
        var destinationLatitude = Math.Asin(
            (Math.Sin(latitude) * Math.Cos(angularDistance))
            + (Math.Cos(latitude) * Math.Sin(angularDistance) * Math.Cos(bearing)));
        var destinationLongitude = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(latitude),
            Math.Cos(angularDistance) - (Math.Sin(latitude) * Math.Sin(destinationLatitude)));
        var normalizedLongitude = (((destinationLongitude * 180 / Math.PI) + 540) % 360) - 180;
        return GeoCoordinate.Create(destinationLatitude * 180 / Math.PI, normalizedLongitude);
    }
}