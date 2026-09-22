namespace Trellis.Primitives;

using System.Text.Json.Serialization;

/// <summary>
/// A geographic latitude and longitude in decimal degrees.
/// </summary>
/// <remarks>
/// Components are finite and stored without rounding or normalization. Equality and ordering
/// use latitude followed by longitude, not geographic equivalence or a distance tolerance.
/// JSON is an object with numeric <c>latitude</c> and <c>longitude</c> properties.
/// </remarks>
[JsonConverter(typeof(CompositeValueObjectJsonConverter<GeoCoordinate>))]
public sealed class GeoCoordinate : ValueObject
{
    private const double MeanEarthRadiusMeters = 6_371_008.8;

    /// <summary>
    /// Gets the latitude in decimal degrees, from -90 through 90 inclusive.
    /// </summary>
    public double Latitude { get; private set; }

    /// <summary>
    /// Gets the longitude in decimal degrees, from -180 through 180 inclusive.
    /// </summary>
    public double Longitude { get; private set; }

    // Used by EF Core for materialization without a dependency on EF Core.
    private GeoCoordinate() { }

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Validates both components and creates a geographic coordinate.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees, from -90 through 90 inclusive.</param>
    /// <param name="longitude">Longitude in decimal degrees, from -180 through 180 inclusive.</param>
    /// <param name="fieldName">Optional owner name or JSON Pointer; component errors are reported beneath it.</param>
    /// <returns>A coordinate or all component validation failures.</returns>
    /// <remarks>
    /// NaN and infinity are rejected. Without an owner, errors use <c>/latitude</c> and
    /// <c>/longitude</c>; <c>location</c> produces <c>/location/latitude</c> and
    /// <c>/location/longitude</c>. Existing JSON Pointers are preserved.
    /// </remarks>
    public static Result<GeoCoordinate> TryCreate(double latitude, double longitude, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(GeoCoordinate) + '.' + nameof(TryCreate));
        var owner = InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty));

        return ValidateComponent(latitude, 90, owner.AppendProperty("latitude"), nameof(Latitude))
            .Combine(ValidateComponent(longitude, 180, owner.AppendProperty("longitude"), nameof(Longitude)))
            .Map((lat, lon) => new GeoCoordinate(lat, lon));
    }

    /// <summary>
    /// Creates a coordinate from trusted values, throwing when either component is invalid.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <returns>The validated coordinate.</returns>
    /// <exception cref="InvalidOperationException">A component is non-finite or outside its valid range.</exception>
    /// <remarks>Use <see cref="TryCreate"/> for untrusted input and inside Result pipelines.</remarks>
    public static GeoCoordinate Create(double latitude, double longitude)
    {
        var result = TryCreate(latitude, longitude);
        if (result.TryGetValue(out var coordinate, out var error))
            return coordinate;

        throw new InvalidOperationException($"Failed to create GeoCoordinate: {error.GetDisplayMessage()}");
    }

    /// <summary>
    /// Computes the approximate shortest great-circle distance to another coordinate in meters.
    /// </summary>
    /// <param name="other">The destination coordinate.</param>
    /// <returns>A finite, non-negative distance in meters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <remarks>
    /// Uses the haversine formula with a fixed mean Earth radius of 6,371,008.8 meters.
    /// This spherical approximation is not an ellipsoidal geodesic or a surveying calculation
    /// and does not account for altitude. It runs in memory, not as an EF Core SQL expression.
    /// </remarks>
    public double DistanceMetersTo(GeoCoordinate other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // Pi-scaled functions keep the poles and equivalent +/-180 longitude endpoints exact.
        var latitudeSine = double.SinPi((other.Latitude - Latitude) / 360);
        var longitudeSine = double.SinPi((other.Longitude - Longitude) / 360);
        var haversine = (latitudeSine * latitudeSine)
            + (double.CosPi(Latitude / 180) * double.CosPi(other.Latitude / 180) * longitudeSine * longitudeSine);

        // Rounding near antipodal points can push the haversine just beyond its valid range.
        return 2 * MeanEarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
    }

    /// <inheritdoc />
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Latitude);
        components.Add(Longitude);
    }

    /// <summary>
    /// Returns the invariant representation <c>(latitude, longitude)</c>.
    /// </summary>
    /// <returns>The unrounded components formatted with invariant culture.</returns>
    public override string ToString() => FormattableString.Invariant($"({Latitude}, {Longitude})");

    private static Result<double> ValidateComponent(double value, int bound, InputPointer field, string name)
    {
        if (!double.IsFinite(value))
            return Result.Fail<double>(Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.NumberFinite, detail: $"{name} must be a finite number."));

        if (value < -bound)
            return Result.Fail<double>(Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.ValueGreaterThanOrEqual,
                args: ValidationArgs.Of("comparisonValue", -bound), detail: $"{name} must be at least {-bound} degrees."));

        if (value > bound)
            return Result.Fail<double>(Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.ValueLessThanOrEqual,
                args: ValidationArgs.Of("comparisonValue", bound), detail: $"{name} must be at most {bound} degrees."));

        return Result.Ok(value);
    }
}