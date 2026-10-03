---
title: Geographic Value Objects
package: Trellis.Primitives
topics: [geospatial, value-object, distance, validation]
last_verified: 2026-10-02
audience: [developer]
---
# Model coordinates and nearby searches

`Trellis.Primitives` provides storage-neutral geographic values for validating latitude and longitude, measuring approximate in-memory distance, and building a safe first-pass radius filter.

Use [GeoCoordinate](xref:Trellis.Primitives.GeoCoordinate) for a point and [GeoBounds](xref:Trellis.Primitives.GeoBounds) for a center plus radius. A bounds object exposes one or two non-wrapping [GeoBoundingBox](xref:Trellis.Primitives.GeoBoundingBox) values, including antimeridian cases.

```bash
dotnet add package Trellis.Primitives
```

## Build a nearby-search value

The following `Program.cs` validates search input, builds conservative bounds, and checks a trusted candidate:

```csharp
using Trellis;
using Trellis.Primitives;

var searchResult = NearbySearch.TryCreate(
    latitude: 47.6062,
    longitude: -122.3321,
    radiusMeters: 10_000);

if (!searchResult.TryGetValue(out var search, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

var candidate = GeoCoordinate.Create(47.6205, -122.3493);
bool insideBroadBounds = search.Bounds.Contains(candidate);
double exactMeters = search.Center.DistanceMetersTo(candidate);

Console.WriteLine($"candidate={insideBroadBounds}, distance={exactMeters:N0}m");

public sealed record NearbySearch(GeoCoordinate Center, GeoBounds Bounds)
{
    public static Result<NearbySearch> TryCreate(
        double latitude,
        double longitude,
        double radiusMeters) =>
        GeoCoordinate.TryCreate(latitude, longitude, "center")
            .Bind(center =>
                GeoBounds.TryCreate(center, radiusMeters, nameof(radiusMeters))
                    .Map(bounds => new NearbySearch(center, bounds)));
}
```

`GeoCoordinate.TryCreate` validates both components and can report both failures. Latitude must be within `-90..90`, longitude within `-180..180`, and neither may be NaN or infinity.

## Use bounds as a prefilter

`GeoBounds.Contains` answers whether a point is inside a conservative rectangle. It can include points outside the requested circle, so follow it with an exact distance comparison:

```csharp
bool isMatch =
    search.Bounds.Contains(candidate) &&
    search.Center.DistanceMetersTo(candidate) <= search.Bounds.RadiusMeters;
```

`DistanceMetersTo` uses a spherical haversine approximation. It is appropriate for common nearby-search behavior, not surveying, altitude-aware calculations, or ellipsoidal geodesics.

[GeoCoordinateExpressions](xref:Trellis.EntityFrameworkCore.GeoCoordinateExpressions) requires the separate `Trellis.EntityFrameworkCore` package. [Install it](integration-ef.md#installation), then follow the [computed-distance query walkthrough](pagination.md#computed-distance-or-score) for numeric latitude and longitude columns. The expressions need no DI or interceptor registration. Use a provider-native spatial type instead when you need a spatial index.

## Serialization and equality

`GeoCoordinate` serializes as an object with numeric `latitude` and `longitude` fields and deserializes through the validating factory. Equality compares the stored components exactly; it does not apply an epsilon or normalize equivalent longitudes.

Complete generated API:

- [GeoCoordinate](xref:Trellis.Primitives.GeoCoordinate)
- [GeoBounds](xref:Trellis.Primitives.GeoBounds)
- [GeoBoundingBox](xref:Trellis.Primitives.GeoBoundingBox)
