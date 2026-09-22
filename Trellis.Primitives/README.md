# Trellis.Primitives

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives)

Strongly typed value objects for .NET, with built-in primitives like `EmailAddress` and `Money` plus composite JSON conversion and tracing registration for primitive value objects.

## Installation
```bash
dotnet add package Trellis.Primitives
```

The `Required*<TSelf>` and `ScalarValueObject<TSelf, TUnderlying>` base classes live in `Trellis.Core`. The source generator, generated primitive JSON converter, and primitive trace source are bundled inside the `Trellis.Core` package (transitively referenced by `Trellis.Primitives`) — no extra package is required.

## Quick Example
```csharp
using Trellis;
using Trellis.Primitives;

// TryCreate returns Result<T>; pattern-match before using the value.
var emailResult = EmailAddress.TryCreate("ada@example.com");

// Money.Create throws on invalid input; use TryCreate for user input.
var subtotal = Money.Create(12.34m, "USD");
var shipping = Money.Create(2.00m, "USD");

// Arithmetic on Money returns Result<Money> (currency-mismatch / overflow safe).
Result<Money> grandTotal = subtotal.Add(shipping);

// Define a custom value object — the source generator emits TryCreate, equality, JSON converters, etc.
// RequiredString rejects null/empty/whitespace and trims by default; RequiredGuid rejects Guid.Empty.
public sealed partial class CustomerEmail : RequiredString<CustomerEmail>;
public sealed partial class OrderId : RequiredGuid<OrderId>;
```

## Key Features
- Ready-to-use value objects for common concepts such as email, URL, money, and percentages.
- `Trellis.Core` base classes like `RequiredString<CustomerEmail>` and `RequiredGuid<OrderId>` for custom domain types.
- Lenient-by-default generated validation (rejects `null` only); opt into sentinel rejection with `[NotDefault]` and string trimming with `[Trim]` when domain strictness is required.
- Validation and parsing rules that stay with the type instead of leaking into handlers and controllers.
- `GeoCoordinate` validates finite latitude/longitude and calculates approximate in-memory great-circle distances in meters.
- `WeeklyPeriod` and `WeeklySchedule` model recurring local-clock availability in an IANA time zone.

## Geographic coordinates

```csharp
var seattle = GeoCoordinate.Create(47.6062, -122.3321);
var portland = GeoCoordinate.Create(45.5152, -122.6784);
double meters = seattle.DistanceMetersTo(portland);
```

Use `TryCreate(latitude, longitude, fieldName)` for untrusted input. It accumulates both
component errors; latitude is `-90..90` and longitude is `-180..180`, inclusive. JSON is
`{ "latitude": number, "longitude": number }`. Values are not rounded or normalized.
Distance uses a sphere of radius 6,371,008.8 meters, not an ellipsoidal model or SQL spatial query.

## Weekly availability

```csharp
var schedule = WeeklySchedule.Create("America/Los_Angeles",
[
    WeeklyPeriod.Create(DayOfWeek.Friday, new TimeOnly(22, 0), new TimeOnly(2, 0)),
    WeeklyPeriod.CreateAllDay(DayOfWeek.Sunday)
]);
bool available = schedule.IsActiveAt(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));
```

Use `TryCreate` / `TryCreateAllDay` for untrusted input. Intervals include their start and
exclude their end; an earlier end means the next day. Equal endpoints require the explicit
all-day factory. Empty schedules are always closed. Overlaps are rejected, touching periods
are retained, and input order is normalized without losing `TimeOnly` tick precision.

The host must supply the IANA time-zone data. Repeated DST clock times both match; skipped
clock times never occur. Use `Contains(day, time)` for a local-clock query without conversion.
JSON and persistence use application-owned DTOs and validated rehydration, not direct
composite JSON conversion or EF owned-type materialization. Holidays and job scheduling are
outside this primitive's scope.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/primitives.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-primitives.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Primitives\tests\Trellis.Primitives.Tests.csproj -c Release
```
