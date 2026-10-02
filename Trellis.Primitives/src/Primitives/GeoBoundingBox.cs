namespace Trellis.Primitives;

/// <summary>
/// One non-wrapping latitude/longitude rectangle used as a conservative geographic query prefilter.
/// </summary>
/// <remarks>
/// Longitude always increases from <see cref="MinimumLongitude"/> to <see cref="MaximumLongitude"/>.
/// A search area that crosses the antimeridian is represented by two boxes in <see cref="GeoBounds"/>,
/// never by a box whose minimum longitude is greater than its maximum.
/// </remarks>
public sealed class GeoBoundingBox : ValueObject
{
    /// <summary>Gets a box covering every valid coordinate.</summary>
    public static GeoBoundingBox World { get; } = new(-90, 90, -180, 180);

    /// <summary>Gets the inclusive southern latitude in decimal degrees.</summary>
    public double MinimumLatitude { get; }

    /// <summary>Gets the inclusive northern latitude in decimal degrees.</summary>
    public double MaximumLatitude { get; }

    /// <summary>Gets the inclusive western longitude in decimal degrees.</summary>
    public double MinimumLongitude { get; }

    /// <summary>Gets the inclusive eastern longitude in decimal degrees.</summary>
    public double MaximumLongitude { get; }

    internal GeoBoundingBox(
        double minimumLatitude,
        double maximumLatitude,
        double minimumLongitude,
        double maximumLongitude)
    {
        ValidateRange(minimumLatitude, maximumLatitude, -90, 90, nameof(minimumLatitude), nameof(maximumLatitude));
        ValidateRange(minimumLongitude, maximumLongitude, -180, 180, nameof(minimumLongitude), nameof(maximumLongitude));
        MinimumLatitude = minimumLatitude;
        MaximumLatitude = maximumLatitude;
        MinimumLongitude = minimumLongitude;
        MaximumLongitude = maximumLongitude;
    }

    /// <summary>Checks whether the coordinate's stored components fall inside this rectangle.</summary>
    /// <param name="coordinate">The coordinate to test.</param>
    /// <returns><see langword="true"/> when both components are within the inclusive bounds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="coordinate"/> is null.</exception>
    public bool Contains(GeoCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return coordinate.Latitude >= MinimumLatitude
            && coordinate.Latitude <= MaximumLatitude
            && coordinate.Longitude >= MinimumLongitude
            && coordinate.Longitude <= MaximumLongitude;
    }

    /// <inheritdoc />
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(MinimumLatitude);
        components.Add(MaximumLatitude);
        components.Add(MinimumLongitude);
        components.Add(MaximumLongitude);
    }

    private static void ValidateRange(
        double minimum,
        double maximum,
        int lowerBound,
        int upperBound,
        string minimumName,
        string maximumName)
    {
        if (!double.IsFinite(minimum) || minimum < lowerBound || minimum > upperBound)
            throw new ArgumentOutOfRangeException(minimumName, minimum, $"Value must be finite and between {lowerBound} and {upperBound}.");
        if (!double.IsFinite(maximum) || maximum < lowerBound || maximum > upperBound)
            throw new ArgumentOutOfRangeException(maximumName, maximum, $"Value must be finite and between {lowerBound} and {upperBound}.");
        if (minimum > maximum)
            throw new ArgumentException("Minimum must not be greater than maximum.", minimumName);
    }
}