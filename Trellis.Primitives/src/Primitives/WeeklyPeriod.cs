namespace Trellis.Primitives;

/// <summary>
/// An immutable, recurring local-clock interval starting on a day of the week.
/// </summary>
/// <remarks>
/// Intervals include their start and exclude their end. An end before the start means the
/// following day. Equal endpoints are invalid unless created explicitly as an all-day period.
/// TimeOnly tick precision is preserved. Use a DTO for JSON and persistence.
/// </remarks>
public sealed class WeeklyPeriod : ValueObject
{
    internal const long TicksPerWeek = 7 * TimeSpan.TicksPerDay;

    /// <summary>Gets the day on which this period starts.</summary>
    public DayOfWeek Day { get; }

    /// <summary>Gets the inclusive local start time, or midnight for an all-day period.</summary>
    public TimeOnly Start { get; }

    /// <summary>Gets the exclusive local end time, or midnight for an all-day period.</summary>
    public TimeOnly End { get; }

    /// <summary>Gets whether the period covers the entire calendar day, midnight to midnight.</summary>
    public bool IsAllDay { get; }

    internal long StartTickOfWeek => ((long)Day * TimeSpan.TicksPerDay) + Start.Ticks;

    internal long DurationTicks => IsAllDay
        ? TimeSpan.TicksPerDay
        : (End.Ticks - Start.Ticks + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;

    private WeeklyPeriod(DayOfWeek day, TimeOnly start, TimeOnly end, bool isAllDay)
    {
        Day = day;
        Start = start;
        End = end;
        IsAllDay = isAllDay;
    }

    /// <summary>Validates the day and unequal endpoints, accumulating independent failures.</summary>
    /// <param name="day">The starting day, Sunday through Saturday.</param>
    /// <param name="start">Inclusive local start time.</param>
    /// <param name="end">Exclusive local end time; an earlier time means the following day.</param>
    /// <param name="fieldName">Optional owner name or JSON Pointer for component errors.</param>
    /// <returns>The period, or errors under <c>day</c> and/or <c>end</c>.</returns>
    public static Result<WeeklyPeriod> TryCreate(
        DayOfWeek day, TimeOnly start, TimeOnly end, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(WeeklyPeriod) + '.' + nameof(TryCreate));
        var owner = InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty));
        var validEnd = Result.Ensure(end != start, () => Error.InvalidInput.ForField(
            field: owner.AppendProperty("end"), code: ValidationCodes.ValueMustNotEqual,
            args: ValidationArgs.Of("comparisonProperty", "start"),
            detail: "Start and end must differ. Use an explicit all-day period for a whole calendar day."));

        return ValidateDay(day, owner.AppendProperty("day"))
            .Combine(validEnd)
            .Map((validDay, _) => new WeeklyPeriod(validDay, start, end, false));
    }

    /// <summary>Creates an explicit midnight-to-midnight period for one calendar day.</summary>
    /// <param name="day">The day to cover, Sunday through Saturday.</param>
    /// <param name="fieldName">Optional owner name or JSON Pointer for the day error.</param>
    /// <returns>The all-day period, or an undefined-day failure.</returns>
    public static Result<WeeklyPeriod> TryCreateAllDay(DayOfWeek day, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(WeeklyPeriod) + '.' + nameof(TryCreateAllDay));
        var owner = InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty));
        return ValidateDay(day, owner.AppendProperty("day"))
            .Map(validDay => new WeeklyPeriod(validDay, TimeOnly.MinValue, TimeOnly.MinValue, true));
    }

    /// <summary>Creates a period from trusted input.</summary>
    /// <param name="day">The starting day.</param>
    /// <param name="start">Inclusive local start time.</param>
    /// <param name="end">Exclusive local end time.</param>
    /// <returns>The validated period.</returns>
    /// <exception cref="InvalidOperationException">The day is undefined or the endpoints are equal.</exception>
    public static WeeklyPeriod Create(DayOfWeek day, TimeOnly start, TimeOnly end) =>
        TryCreate(day, start, end).GetValueOrThrow();

    /// <summary>Creates an explicit all-day period from a trusted day.</summary>
    /// <param name="day">The day to cover.</param>
    /// <returns>The validated all-day period.</returns>
    /// <exception cref="InvalidOperationException">The day is undefined.</exception>
    public static WeeklyPeriod CreateAllDay(DayOfWeek day) =>
        TryCreateAllDay(day).GetValueOrThrow();

    /// <inheritdoc />
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Day);
        components.Add(Start);
        components.Add(End);
        components.Add(IsAllDay);
    }

    private static Result<DayOfWeek> ValidateDay(DayOfWeek day, InputPointer field) =>
        Result.Ensure(Enum.IsDefined(day), () => Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.EnumUndefined, args: ValidationArgs.Allowed(Enum.GetNames<DayOfWeek>()),
                detail: "Day must be a defined day of the week."))
            .Map(_ => day);
}