---
title: Weekly Schedules
package: Trellis.Primitives
topics: [schedule, time-zone, value-object, validation]
last_verified: 2026-10-02
audience: [developer]
---
# Model weekly local-time availability

[WeeklySchedule](xref:Trellis.Primitives.WeeklySchedule) answers a focused question: is this recurring weekly schedule active at a local day and time, or at a specific instant in its IANA time zone?

It is useful for opening hours and recurring availability. It is not a holiday calendar, capacity model, recurrence engine, or elapsed-duration scheduler.

```bash
dotnet add package Trellis.Primitives
```

## Create overnight and all-day periods

This complete console example creates Friday overnight hours plus an all-day Sunday:

```csharp
using Trellis;
using Trellis.Primitives;

var scheduleResult = StoreHours.Create();

if (!scheduleResult.TryGetValue(out var schedule, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

var instant = new DateTimeOffset(
    2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

Console.WriteLine(schedule.IsActiveAt(instant));

public static class StoreHours
{
    public static Result<WeeklySchedule> Create() =>
        WeeklyPeriod.TryCreate(
                DayOfWeek.Friday,
                new TimeOnly(22, 0),
                new TimeOnly(2, 0),
                "friday")
            .Bind(friday =>
                WeeklyPeriod.TryCreateAllDay(DayOfWeek.Sunday, "sunday")
                    .Bind(sunday =>
                        WeeklySchedule.TryCreate(
                            "America/Los_Angeles",
                            [friday, sunday],
                            "hours")));
}
```

A normal period includes its start and excludes its end. An end earlier than the start crosses midnight, so Friday `22:00` to `02:00` includes early Saturday morning.

Equal endpoints do not mean all day. Use `TryCreateAllDay` to state that intent explicitly.

## Let the schedule validate the week

`WeeklySchedule.TryCreate` copies and sorts the periods, rejects overlaps across day and week boundaries, and permits periods that only touch. An empty period list means always closed.

The time-zone identifier must resolve to an IANA zone. `UTC` is valid; Windows-only identifiers are rejected. `IsActiveAt` converts the supplied instant with the schedule's retained zone rules, so repeated and skipped local times follow daylight-saving behavior.

Use `Contains(day, time)` when you already have local weekly clock coordinates. Use `IsActiveAt(instant)` when you have an absolute instant.

## Keep transport and persistence explicit

Project schedules through an application-owned DTO containing the zone ID and each period's day, start, end, and all-day flag. Rebuild the value through `TryCreate` methods at the boundary.

Do not apply the composite value-object JSON converter to these types; collections and `TimeOnly` are outside that converter's shape. They also do not expose parameterless constructors for direct EF owned-type materialization. Choose an explicit JSON snapshot or separate persistence records and rehydrate through the factories.

Complete generated API:

- [WeeklyPeriod](xref:Trellis.Primitives.WeeklyPeriod)
- [WeeklySchedule](xref:Trellis.Primitives.WeeklySchedule)
