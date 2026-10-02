namespace Trellis.Primitives;

using System.Collections.ObjectModel;

/// <summary>
/// A validated spherical-radius search area represented by one or two conservative non-wrapping boxes.
/// </summary>
/// <remarks>
/// The boxes are a broad prefilter: every coordinate within <see cref="RadiusMeters"/> is retained,
/// but coordinates outside the radius can also be present and require an exact distance check.
/// </remarks>
public sealed class GeoBounds : ValueObject
{
    private const double DegreesToRadians = Math.PI / 180;
    private const double RadiansToDegrees = 180 / Math.PI;
    private const double HalfTurnRadians = Math.PI;
    private const double QuarterTurnRadians = Math.PI / 2;
    private const double FullTurnRadians = 2 * Math.PI;
    private const double MachineEpsilon = 2.2204460492503131e-16;
    private const double BaseAngularSafetyMarginRadians = 64 * MachineEpsilon;
    private const double SquareRootMachineEpsilon = 1.4901161193847656e-8;
    private const double TrigonometricSafetyMargin = 64 * MachineEpsilon;

    private readonly ReadOnlyCollection<GeoBoundingBox> _boxes;

    /// <summary>Gets the center used to calculate these bounds.</summary>
    public GeoCoordinate Center { get; }

    /// <summary>Gets the finite, non-negative spherical radius in meters.</summary>
    public double RadiusMeters { get; }

    /// <summary>
    /// Gets one non-wrapping box, or two boxes ordered west-to-east when the area crosses the antimeridian.
    /// </summary>
    public IReadOnlyList<GeoBoundingBox> Boxes => _boxes;

    private GeoBounds(GeoCoordinate center, double radiusMeters, params GeoBoundingBox[] boxes)
    {
        Center = center;
        RadiusMeters = radiusMeters;
        _boxes = Array.AsReadOnly(boxes);
    }

    /// <summary>Validates a radius and calculates conservative spherical search bounds.</summary>
    /// <param name="center">The center of the radius search.</param>
    /// <param name="radiusMeters">A finite radius greater than or equal to zero.</param>
    /// <param name="fieldName">Optional property name or JSON Pointer for a radius validation failure.</param>
    /// <returns>One or two non-wrapping boxes, or a validation failure for the radius.</returns>
    /// <remarks>
    /// A radius reaching either pole covers every longitude. A radius at least half the mean-Earth
    /// circumference covers the world. Bounds include an outward numerical safety margin, scaled
    /// near antipodal distances, so spherical floating-point roundoff does not exclude boundaries.
    /// </remarks>
    public static Result<GeoBounds> TryCreate(
        GeoCoordinate center,
        double radiusMeters,
        string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(center);
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(GeoBounds) + '.' + nameof(TryCreate));
        var field = InputPointer.ForProperty(fieldName.NormalizeFieldName("radiusMeters"));

        if (!double.IsFinite(radiusMeters))
            return Result.Fail<GeoBounds>(Error.InvalidInput.ForField(
                field: field,
                code: ValidationCodes.NumberFinite,
                detail: "Radius must be a finite number."));

        if (radiusMeters < 0)
            return Result.Fail<GeoBounds>(Error.InvalidInput.ForField(
                field: field,
                code: ValidationCodes.ValueGreaterThanOrEqual,
                args: ValidationArgs.Of("comparisonValue", 0),
                detail: "Radius must be at least zero meters."));

        return Result.Ok(Build(center, radiusMeters));
    }

    /// <summary>Calculates spherical search bounds from trusted input.</summary>
    /// <param name="center">The center of the radius search.</param>
    /// <param name="radiusMeters">A finite radius greater than or equal to zero.</param>
    /// <returns>One or two conservative, non-wrapping boxes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="center"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="radiusMeters"/> is invalid.</exception>
    public static GeoBounds Create(GeoCoordinate center, double radiusMeters) =>
        TryCreate(center, radiusMeters).GetValueOrThrow();

    /// <summary>Checks whether a coordinate belongs to at least one conservative prefilter box.</summary>
    /// <param name="coordinate">The coordinate to test.</param>
    /// <returns><see langword="true"/> when a box contains the stored latitude and longitude.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="coordinate"/> is null.</exception>
    /// <remarks>
    /// A successful result does not prove the coordinate is within <see cref="RadiusMeters"/>;
    /// apply <see cref="GeoCoordinate.DistanceMetersTo"/> for the exact spherical check.
    /// </remarks>
    public bool Contains(GeoCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        foreach (var box in _boxes)
            if (box.Contains(coordinate))
                return true;

        return false;
    }

    /// <inheritdoc />
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Center);
        components.Add(RadiusMeters);
        foreach (var box in _boxes)
            components.Add(box);
    }

    private static GeoBounds Build(GeoCoordinate center, double radiusMeters)
    {
        var angularRadius = radiusMeters / GeoCoordinate.MeanEarthRadiusMeters;
        if (angularRadius >= HalfTurnRadians)
            return new GeoBounds(center, radiusMeters, GeoBoundingBox.World);

        var angularSafetyMargin = CalculateAngularSafetyMargin(angularRadius);
        var expandedAngularRadius = angularRadius + angularSafetyMargin;
        var latitude = center.Latitude * DegreesToRadians;
        var longitude = center.Longitude * DegreesToRadians;
        var rawMinimumLatitude = latitude - expandedAngularRadius;
        var rawMaximumLatitude = latitude + expandedAngularRadius;
        var minimumLatitude = ExpandMinimum(
            Math.Max(-QuarterTurnRadians, rawMinimumLatitude) * RadiansToDegrees,
            -90);
        var maximumLatitude = ExpandMaximum(
            Math.Min(QuarterTurnRadians, rawMaximumLatitude) * RadiansToDegrees,
            90);

        if (rawMinimumLatitude <= -QuarterTurnRadians || rawMaximumLatitude >= QuarterTurnRadians)
            return new GeoBounds(
                center,
                radiusMeters,
                new GeoBoundingBox(minimumLatitude, maximumLatitude, -180, 180));

        var longitudeRatio = Math.Sin(expandedAngularRadius) / double.CosPi(center.Latitude / 180);
        var longitudeDelta = Math.Asin(Math.Clamp(
            longitudeRatio + TrigonometricSafetyMargin,
            0,
            1)) + angularSafetyMargin;
        var westernLongitude = longitude - longitudeDelta;
        var easternLongitude = longitude + longitudeDelta;

        if (westernLongitude <= -HalfTurnRadians)
        {
            var wrappedWest = (westernLongitude + FullTurnRadians) * RadiansToDegrees;
            return new GeoBounds(
                center,
                radiusMeters,
                new GeoBoundingBox(
                    minimumLatitude,
                    maximumLatitude,
                    -180,
                    ExpandMaximum(easternLongitude * RadiansToDegrees, 180)),
                new GeoBoundingBox(
                    minimumLatitude,
                    maximumLatitude,
                    ExpandMinimum(wrappedWest, -180),
                    180));
        }

        if (easternLongitude >= HalfTurnRadians)
        {
            var wrappedEast = (easternLongitude - FullTurnRadians) * RadiansToDegrees;
            return new GeoBounds(
                center,
                radiusMeters,
                new GeoBoundingBox(
                    minimumLatitude,
                    maximumLatitude,
                    -180,
                    ExpandMaximum(wrappedEast, 180)),
                new GeoBoundingBox(
                    minimumLatitude,
                    maximumLatitude,
                    ExpandMinimum(westernLongitude * RadiansToDegrees, -180),
                    180));
        }

        return new GeoBounds(
            center,
            radiusMeters,
            new GeoBoundingBox(
                minimumLatitude,
                maximumLatitude,
                ExpandMinimum(westernLongitude * RadiansToDegrees, -180),
                ExpandMaximum(easternLongitude * RadiansToDegrees, 180)));
    }

    private static double CalculateAngularSafetyMargin(double angularRadius)
    {
        // Inverse haversine loses precision as the central angle approaches pi.
        var conditioning = Math.Max(
            Math.Abs(Math.Cos(angularRadius / 2)),
            SquareRootMachineEpsilon);
        return BaseAngularSafetyMarginRadians / conditioning;
    }

    private static double ExpandMinimum(double value, double floor) =>
        value <= floor ? floor : Math.Max(floor, double.BitDecrement(value));

    private static double ExpandMaximum(double value, double ceiling) =>
        value >= ceiling ? ceiling : Math.Min(ceiling, double.BitIncrement(value));
}