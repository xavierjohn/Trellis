namespace Trellis.Primitives;

using System.Collections.ObjectModel;

/// <summary>
/// Immutable weekly availability in an IANA time zone, evaluated against local wall-clock time.
/// </summary>
/// <remarks>
/// Empty means always closed. Periods are copied and sorted Sunday-first; overlapping periods
/// are rejected, but touching periods are retained. Equality includes the resolved time-zone
/// identifier and sorted period components, not equivalence of covered instants or zone aliases.
/// Use a DTO for JSON and persistence; this type has no direct EF materialization contract.
/// </remarks>
public sealed class WeeklySchedule : ValueObject
{
    private readonly TimeZoneInfo _timeZone;
    private readonly ReadOnlyCollection<WeeklyPeriod> _periods;

    /// <summary>Gets the resolved IANA identifier. Equivalent aliases are not unified.</summary>
    public string TimeZoneId => _timeZone.Id;

    /// <summary>Gets a read-only snapshot of periods sorted by local start within the week.</summary>
    public IReadOnlyList<WeeklyPeriod> Periods => _periods;

    private WeeklySchedule(TimeZoneInfo timeZone, WeeklyPeriod[] periods)
    {
        _timeZone = timeZone;
        _periods = Array.AsReadOnly(periods);
    }

    /// <summary>Validates an IANA time-zone ID and a collection of non-overlapping periods.</summary>
    /// <param name="timeZoneId">An IANA ID resolvable on this system, including <c>UTC</c>. Surrounding whitespace is trimmed.</param>
    /// <param name="periods">Periods to copy. Empty is valid; a null collection or null element is invalid.</param>
    /// <param name="fieldName">Optional owner name or JSON Pointer for nested errors.</param>
    /// <returns>A schedule or accumulated time-zone and period validation failures.</returns>
    /// <remarks>
    /// Windows-only IDs are rejected. Resolution uses the host's installed time-zone data;
    /// the resolved rules are retained by this instance. No arbitrary period-count limit is imposed.
    /// </remarks>
    public static Result<WeeklySchedule> TryCreate(
        string? timeZoneId, IReadOnlyList<WeeklyPeriod>? periods, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(WeeklySchedule) + '.' + nameof(TryCreate));
        var owner = InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty));
        return ValidateTimeZone(timeZoneId, owner.AppendProperty("timeZoneId"))
            .Combine(ValidatePeriods(periods, owner.AppendProperty("periods")))
            .Map((zone, sorted) => new WeeklySchedule(zone, sorted));
    }

    /// <summary>Creates a schedule from trusted input.</summary>
    /// <param name="timeZoneId">An IANA ID resolvable on this system.</param>
    /// <param name="periods">Non-overlapping periods; empty means always closed.</param>
    /// <returns>The validated schedule.</returns>
    /// <exception cref="InvalidOperationException">The time zone or periods are invalid.</exception>
    public static WeeklySchedule Create(string timeZoneId, IReadOnlyList<WeeklyPeriod> periods) =>
        TryCreate(timeZoneId, periods).GetValueOrThrow();

    /// <summary>Checks membership of a local day and clock time without applying a time-zone conversion.</summary>
    /// <param name="day">A defined day of the week.</param>
    /// <param name="time">Local clock time, with full tick precision.</param>
    /// <returns>Whether a start-inclusive, end-exclusive period contains that local time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The day is undefined.</exception>
    public bool Contains(DayOfWeek day, TimeOnly time)
    {
        if (!Enum.IsDefined(day))
            throw new ArgumentOutOfRangeException(nameof(day), day, "Day must be a defined day of the week.");

        return ContainsTickOfWeek(((long)day * TimeSpan.TicksPerDay) + time.Ticks);
    }

    /// <summary>Checks whether the schedule is active at an absolute instant.</summary>
    /// <param name="instant">An instant; its supplied offset does not select the schedule's zone.</param>
    /// <returns>Whether the corresponding local wall-clock time belongs to a period.</returns>
    /// <remarks>
    /// Both occurrences of repeated clock times match the same weekly periods. Skipped clock
    /// times have no corresponding instant. This is a pure in-memory query, not SQL translation
    /// or job scheduling; no current clock is read.
    /// </remarks>
    public bool IsActiveAt(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        // Work modulo a week to avoid ConvertTime's clamping at DateTime's calendar bounds.
        var localTick = (((long)utc.DayOfWeek * TimeSpan.TicksPerDay) + utc.TimeOfDay.Ticks
            + _timeZone.GetUtcOffset(instant).Ticks + WeeklyPeriod.TicksPerWeek) % WeeklyPeriod.TicksPerWeek;
        return ContainsTickOfWeek(localTick);
    }

    /// <inheritdoc />
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(TimeZoneId);
        foreach (var period in _periods)
            components.Add(period);
    }

    private bool ContainsTickOfWeek(long tick)
    {
        foreach (var period in _periods)
            if ((tick - period.StartTickOfWeek + WeeklyPeriod.TicksPerWeek) % WeeklyPeriod.TicksPerWeek < period.DurationTicks)
                return true;

        return false;
    }

    private static Result<TimeZoneInfo> ValidateTimeZone(string? id, InputPointer field) =>
        id.ToResult(() => Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.ValueNotNull, detail: "Time zone is required."))
            .Ensure(value => !string.IsNullOrWhiteSpace(value), _ => Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.ValueNotEmpty, detail: "Time zone must not be empty."))
            .Bind(value =>
            {
                var zone = TimeZoneInfo.TryFindSystemTimeZoneById(value.Trim(), out var resolved) && resolved.HasIanaId
                    ? resolved
                    : null;
                return zone.ToResult(() => Error.InvalidInput.ForField(
                    field: field, code: ValidationCodes.StringTimeZoneIana,
                    detail: "Time zone must be an IANA identifier available on this system."));
            });

    private static Result<WeeklyPeriod[]> ValidatePeriods(IReadOnlyList<WeeklyPeriod>? periods, InputPointer field) =>
        periods.ToResult(() => Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.ValueNotNull, detail: "Periods are required; use an empty collection for an always-closed schedule."))
            .Map(values => values.ToArray())
            .Check(snapshot => snapshot.Select((period, index) =>
                Result.Ensure(period is not null, () => Error.InvalidInput.ForField(
                    field: field.AppendIndex(index), code: ValidationCodes.ValueNotNull, detail: "A period must not be null.")))
                .SequenceAll())
            .Tap(snapshot => Array.Sort(snapshot, static (left, right) => left.StartTickOfWeek.CompareTo(right.StartTickOfWeek)))
            .Ensure(DoNotOverlap, _ => Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.SchedulePeriodsOverlap, detail: "Weekly periods must not overlap, including across the week boundary."));

    private static bool DoNotOverlap(WeeklyPeriod[] sorted)
    {
        for (var index = 1; index < sorted.Length; index++)
            if (sorted[index - 1].StartTickOfWeek + sorted[index - 1].DurationTicks > sorted[index].StartTickOfWeek)
                return false;

        return sorted.Length < 2
            || sorted[^1].StartTickOfWeek + sorted[^1].DurationTicks <= sorted[0].StartTickOfWeek + WeeklyPeriod.TicksPerWeek;
    }
}