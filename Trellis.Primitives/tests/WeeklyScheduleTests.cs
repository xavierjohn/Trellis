namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis.Testing;

public class WeeklyScheduleTests
{
    [Theory]
    [InlineData("UTC", "UTC")]
    [InlineData("Etc/UTC", "Etc/UTC")]
    [InlineData(" America/Los_Angeles ", "America/Los_Angeles")]
    [InlineData("Asia/Kathmandu", "Asia/Kathmandu")]
    public void TryCreate_EmptySchedule_IsAlwaysClosed(string zone, string expectedId)
    {
        var schedule = WeeklySchedule.TryCreate(zone, []).Should().BeSuccess().Which;

        schedule.TimeZoneId.Should().Be(expectedId);
        schedule.Periods.Should().BeEmpty();
        foreach (var day in Enum.GetValues<DayOfWeek>())
            schedule.Contains(day, new TimeOnly(12, 0)).Should().BeFalse();
        schedule.IsActiveAt(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, ValidationCodes.ValueNotNull)]
    [InlineData("", ValidationCodes.ValueNotEmpty)]
    [InlineData(" \t", ValidationCodes.ValueNotEmpty)]
    [InlineData("Mars/Olympus_Mons", ValidationCodes.StringTimeZoneIana)]
    [InlineData("Pacific Standard Time", ValidationCodes.StringTimeZoneIana)]
    public void TryCreate_InvalidZone_ReportsReason(string? zone, string code)
    {
        var error = WeeklySchedule.TryCreate(zone, []).Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/timeZoneId");
        error.Fields[0].ReasonCode.Should().Be(code);
    }

    [Fact]
    public void TryCreate_NullCollection_IsNotAnEmptySchedule()
    {
        var error = WeeklySchedule.TryCreate("UTC", null).Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/periods");
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.ValueNotNull);
    }

    [Theory]
    [InlineData(null, ValidationCodes.ValueNotNull)]
    [InlineData(" \t", ValidationCodes.ValueNotEmpty)]
    public void TryCreate_MissingZoneAndPeriods_AccumulatesIndependentFailures(string? zone, string zoneCode)
    {
        var error = WeeklySchedule.TryCreate(zone, null, "Hours")
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/hours/timeZoneId", "/hours/periods"]);
        error.Fields.Items.Select(v => v.ReasonCode).Should().Equal([zoneCode, ValidationCodes.ValueNotNull]);
    }

    [Fact]
    public void TryCreate_NullElementsAndInvalidZone_AccumulatesOriginalIndexes()
    {
        var error = WeeklySchedule.TryCreate("unknown", [null!, Period(DayOfWeek.Monday, 9, 17), null!], "/hours")
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path)
            .Should().Equal(["/hours/timeZoneId", "/hours/periods/0", "/hours/periods/2"]);
    }

    [Fact]
    public void TryCreate_NullElementAndOverlaps_SkipsOverlapValidation()
    {
        var error = WeeklySchedule.TryCreate("UTC",
                [Period(DayOfWeek.Monday, 9, 17), null!, Period(DayOfWeek.Monday, 10, 12)])
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Should().ContainSingle();
        error.Fields[0].Field.Path.Should().Be("/periods/1");
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.ValueNotNull);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 10, 12)]
    [InlineData(DayOfWeek.Monday, 8, 10)]
    [InlineData(DayOfWeek.Monday, 9, 17)]
    [InlineData(DayOfWeek.Monday, 8, 18)]
    [InlineData(DayOfWeek.Sunday, 23, 10)]
    public void TryCreate_OverlappingPeriods_Fails(DayOfWeek day, int start, int end)
    {
        var error = WeeklySchedule.TryCreate("UTC", [Period(DayOfWeek.Monday, 9, 17), Period(day, start, end)])
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/periods");
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.SchedulePeriodsOverlap);
    }

    [Fact]
    public void TryCreate_WeekWrapOverlapAndInvalidZone_AccumulatesBothFailures()
    {
        var error = WeeklySchedule.TryCreate("unknown",
            [Period(DayOfWeek.Saturday, 22, 2), Period(DayOfWeek.Sunday, 1, 3)], "Hours")
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/hours/timeZoneId", "/hours/periods"]);
    }

    [Fact]
    public void TryCreate_AllDayAndRegularOverlap_Fails() =>
        WeeklySchedule.TryCreate("UTC",
                [WeeklyPeriod.CreateAllDay(DayOfWeek.Monday), Period(DayOfWeek.Monday, 9, 17)])
            .Should().BeFailureOfType<Error.InvalidInput>();

    [Fact]
    public void TryCreate_TouchingPeriodsIncludingWeekWrap_Succeeds()
    {
        var schedule = WeeklySchedule.TryCreate("UTC",
            [Period(DayOfWeek.Monday, 9, 12), Period(DayOfWeek.Monday, 12, 17),
             Period(DayOfWeek.Saturday, 22, 2), Period(DayOfWeek.Sunday, 2, 3)])
            .Should().BeSuccess().Which;

        schedule.Periods.Should().HaveCount(4);
    }

    [Fact]
    public void TryCreate_InputOrderAndMutation_DoesNotAffectCanonicalValue()
    {
        var monday = Period(DayOfWeek.Monday, 9, 17);
        var friday = Period(DayOfWeek.Friday, 9, 17);
        WeeklyPeriod[] input = [friday, monday];
        var schedule = WeeklySchedule.Create("UTC", input);
        var hash = schedule.GetHashCode();
        input.Should().Equal([friday, monday]);
        input[0] = WeeklyPeriod.CreateAllDay(DayOfWeek.Sunday);
        var equal = WeeklySchedule.Create("UTC", [monday, friday]);

        schedule.Periods.Should().Equal([monday, friday]);
        (schedule == equal).Should().BeTrue();
        schedule.GetHashCode().Should().Be(hash).And.Be(equal.GetHashCode());
        schedule.CompareTo(equal).Should().Be(0);
        var collection = Assert.IsAssignableFrom<IList<WeeklyPeriod>>(schedule.Periods);
        Action mutate = () => collection[0] = friday;
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TryCreate_OverlappingPeriods_DoesNotSortCallerArray()
    {
        var friday = Period(DayOfWeek.Friday, 9, 17);
        var monday = Period(DayOfWeek.Monday, 9, 17);
        var overlapping = Period(DayOfWeek.Monday, 10, 12);
        WeeklyPeriod[] input = [friday, monday, overlapping];

        WeeklySchedule.TryCreate("UTC", input).Should().BeFailureOfType<Error.InvalidInput>();

        input.Should().Equal([friday, monday, overlapping]);
    }

    [Fact]
    public void Equality_DifferentZoneOrSegmentation_IsNotEqual()
    {
        var schedule = WeeklySchedule.Create("UTC", [Period(DayOfWeek.Monday, 9, 17)]);

        (schedule == WeeklySchedule.Create("Etc/UTC", [Period(DayOfWeek.Monday, 9, 17)])).Should().BeFalse();
        (schedule == WeeklySchedule.Create("UTC",
            [Period(DayOfWeek.Monday, 9, 12), Period(DayOfWeek.Monday, 12, 17)])).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_MoreThanRestaurantSpecificLimit_Succeeds()
    {
        var periods = Enum.GetValues<DayOfWeek>()
            .SelectMany(day => Enumerable.Range(0, 6)
                .Select(hour => WeeklyPeriod.Create(day, new TimeOnly(hour, 0), new TimeOnly(hour, 30))))
            .ToArray();

        WeeklySchedule.TryCreate("UTC", periods).Should().BeSuccess().Which.Periods.Should().HaveCount(42);
    }

    [Fact]
    public void Contains_SubsecondBoundary_IsStartInclusiveAndEndExclusive()
    {
        var start = new TimeOnly(123456789);
        var end = start.Add(TimeSpan.FromTicks(1));
        var schedule = WeeklySchedule.Create("UTC", [WeeklyPeriod.Create(DayOfWeek.Monday, start, end)]);

        schedule.Contains(DayOfWeek.Monday, start.Add(TimeSpan.FromTicks(-1))).Should().BeFalse();
        schedule.Contains(DayOfWeek.Monday, start).Should().BeTrue();
        schedule.Contains(DayOfWeek.Monday, end).Should().BeFalse();
        schedule.IsActiveAt(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero).AddTicks(start.Ticks))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday, 21, false)]
    [InlineData(DayOfWeek.Saturday, 22, true)]
    [InlineData(DayOfWeek.Sunday, 0, true)]
    [InlineData(DayOfWeek.Sunday, 1, true)]
    [InlineData(DayOfWeek.Sunday, 2, false)]
    [InlineData(DayOfWeek.Monday, 0, false)]
    public void Contains_OvernightAcrossWeek_UsesModularIntervals(DayOfWeek day, int hour, bool expected)
    {
        var schedule = WeeklySchedule.Create("UTC", [Period(DayOfWeek.Saturday, 22, 2)]);

        schedule.Contains(day, new TimeOnly(hour, 0)).Should().Be(expected);
    }

    [Fact]
    public void Contains_ExplicitAllDay_CoversOnlyItsCalendarDay()
    {
        var schedule = WeeklySchedule.Create("UTC", [WeeklyPeriod.CreateAllDay(DayOfWeek.Saturday)]);

        schedule.Contains(DayOfWeek.Saturday, TimeOnly.MinValue).Should().BeTrue();
        schedule.Contains(DayOfWeek.Saturday, TimeOnly.MaxValue).Should().BeTrue();
        schedule.Contains(DayOfWeek.Sunday, TimeOnly.MinValue).Should().BeFalse();
    }

    [Fact]
    public void Contains_SevenAllDayPeriods_CoversEntireWeek()
    {
        var schedule = WeeklySchedule.Create("UTC",
            Enum.GetValues<DayOfWeek>().Select(WeeklyPeriod.CreateAllDay).ToArray());

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            schedule.Contains(day, TimeOnly.MinValue).Should().BeTrue();
            schedule.Contains(day, TimeOnly.MaxValue).Should().BeTrue();
        }
    }

    [Fact]
    public void Contains_UndefinedDay_Throws()
    {
        var schedule = WeeklySchedule.Create("UTC", []);
        Action act = () => schedule.Contains((DayOfWeek)7, TimeOnly.MinValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("2026-11-01T08:30:00Z", true)]
    [InlineData("2026-11-01T09:30:00Z", true)]
    [InlineData("2026-11-01T09:45:00Z", false)]
    public void IsActiveAt_FallBack_BothOccurrencesUseLocalWallClock(string instant, bool expected)
    {
        var schedule = WeeklySchedule.Create("America/Los_Angeles",
            [WeeklyPeriod.Create(DayOfWeek.Sunday, new TimeOnly(1, 0), new TimeOnly(1, 45))]);

        schedule.IsActiveAt(Instant(instant)).Should().Be(expected);
        schedule.IsActiveAt(Instant(instant).ToOffset(TimeSpan.FromHours(5))).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-03-08T09:59:59Z")]
    [InlineData("2026-03-08T10:00:00Z")]
    [InlineData("2026-03-08T10:30:00Z")]
    public void IsActiveAt_SpringForward_SkippedClockWindowNeverOccurs(string instant)
    {
        var schedule = WeeklySchedule.Create("America/Los_Angeles", [Period(DayOfWeek.Sunday, 2, 3)]);

        schedule.IsActiveAt(Instant(instant)).Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-03-08T08:00:00Z", 23)]
    [InlineData("2026-11-01T07:00:00Z", 25)]
    public void IsActiveAt_AllDayOnDstTransition_CoversLocalDayRatherThanTwentyFourHours(string start, int hours)
    {
        var schedule = WeeklySchedule.Create("America/Los_Angeles", [WeeklyPeriod.CreateAllDay(DayOfWeek.Sunday)]);
        var midnight = Instant(start);

        schedule.IsActiveAt(midnight.AddTicks(-1)).Should().BeFalse();
        schedule.IsActiveAt(midnight).Should().BeTrue();
        schedule.IsActiveAt(midnight.AddHours(hours).AddTicks(-1)).Should().BeTrue();
        schedule.IsActiveAt(midnight.AddHours(hours)).Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-04-04T14:45:00Z")]
    [InlineData("2026-04-04T15:15:00Z")]
    public void IsActiveAt_HalfHourFallBack_HandlesNonHourlyDst(string instant)
    {
        var schedule = WeeklySchedule.Create("Australia/Lord_Howe",
            [WeeklyPeriod.Create(DayOfWeek.Sunday, new TimeOnly(1, 40), new TimeOnly(1, 50))]);

        schedule.IsActiveAt(Instant(instant)).Should().BeTrue();
    }

    [Fact]
    public void IsActiveAt_QuarterHourOffset_UsesZoneRatherThanInputOffset()
    {
        var schedule = WeeklySchedule.Create("Asia/Kathmandu", [Period(DayOfWeek.Monday, 9, 10)]);

        schedule.IsActiveAt(Instant("2026-09-21T03:15:00Z")).Should().BeTrue();
        schedule.IsActiveAt(Instant("2026-09-21T04:15:00Z")).Should().BeFalse();
    }

    [Fact]
    public void IsActiveAt_ExtremeInstants_DoesNotClampLocalWeekday()
    {
        var beforeMinimum = WeeklySchedule.Create("Etc/GMT+12", [Period(DayOfWeek.Sunday, 12, 13)]);
        var afterMaximum = WeeklySchedule.Create("Etc/GMT-14", [Period(DayOfWeek.Saturday, 13, 14)]);

        beforeMinimum.IsActiveAt(DateTimeOffset.MinValue).Should().BeTrue();
        afterMaximum.IsActiveAt(DateTimeOffset.MaxValue).Should().BeTrue();
    }

    [Fact]
    public void Contains_AllWeekMinutes_MatchesIndependentOvernightOracle()
    {
        foreach (var openingDay in Enum.GetValues<DayOfWeek>())
        {
            var schedule = WeeklySchedule.Create("UTC", [Period(openingDay, 22, 2)]);
            var followingDay = (DayOfWeek)(((int)openingDay + 1) % 7);
            foreach (var day in Enum.GetValues<DayOfWeek>())
                for (var minute = 0; minute < 1440; minute++)
                {
                    var expected = (day == openingDay && minute >= 22 * 60)
                        || (day == followingDay && minute < 2 * 60);
                    schedule.Contains(day, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute))).Should().Be(expected);
                }
        }
    }

    [Fact]
    public void Create_InvalidSchedule_ThrowsAtTrustedBoundary()
    {
        Action act = () => WeeklySchedule.Create("unknown", []);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Result<WeeklySchedule> was a failure.*Time zone must be an IANA identifier available on this system.*");
    }

    [Fact]
    public void Json_DtoSnapshot_RoundTripsThroughValidatedFactories()
    {
        var schedule = WeeklySchedule.Create("America/Los_Angeles",
            [WeeklyPeriod.Create(DayOfWeek.Friday, new TimeOnly(22, 0).Add(TimeSpan.FromTicks(1)), new TimeOnly(2, 0)),
             WeeklyPeriod.CreateAllDay(DayOfWeek.Sunday)]);
        var snapshot = new ScheduleSnapshot
        {
            TimeZoneId = schedule.TimeZoneId,
            Periods = schedule.Periods.Select(p => new PeriodSnapshot
            {
                Day = p.Day,
                Start = p.Start,
                End = p.End,
                IsAllDay = p.IsAllDay
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(snapshot, WeeklyScheduleJsonContext.Default.ScheduleSnapshot);
        var restored = JsonSerializer.Deserialize(json, WeeklyScheduleJsonContext.Default.ScheduleSnapshot);

        var value = Assert.IsType<ScheduleSnapshot>(restored);
        FromSnapshot(value).Should().HaveValue(schedule);
        json.Should().Contain("22:00:00.0000001");
    }

    [Fact]
    public void Json_DtoSnapshot_MissingDayDoesNotBecomeSunday()
    {
        const string json = """
            {"timeZoneId":"UTC","periods":[{"start":"09:00:00","end":"17:00:00","isAllDay":false}]}
            """;
        Action act = () => JsonSerializer.Deserialize(json, WeeklyScheduleJsonContext.Default.ScheduleSnapshot);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_DtoSnapshot_InvalidPeriodCannotBypassValidation()
    {
        const string json = """
            {"timeZoneId":"UTC","periods":[{"day":7,"start":"09:00:00","end":"09:00:00","isAllDay":false}]}
            """;
        var snapshot = Assert.IsType<ScheduleSnapshot>(
            JsonSerializer.Deserialize(json, WeeklyScheduleJsonContext.Default.ScheduleSnapshot));

        var error = FromSnapshot(snapshot).Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/periods/0/day", "/periods/0/end"]);
    }

    private static Result<WeeklySchedule> FromSnapshot(ScheduleSnapshot snapshot) =>
        snapshot.Periods.Select((p, index) =>
        {
            var owner = InputPointer.Root.AppendProperty("periods").AppendIndex(index).Path;
            return p.IsAllDay
                ? WeeklyPeriod.TryCreateAllDay(p.Day, owner)
                : WeeklyPeriod.TryCreate(p.Day, p.Start, p.End, owner);
        }).SequenceAll().Bind(periods => WeeklySchedule.TryCreate(snapshot.TimeZoneId, periods));

    private static WeeklyPeriod Period(DayOfWeek day, int start, int end) =>
        WeeklyPeriod.Create(day, new TimeOnly(start, 0), new TimeOnly(end, 0));

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    public sealed record ScheduleSnapshot
    {
        public required string TimeZoneId { get; init; }
        public required PeriodSnapshot[] Periods { get; init; }
    }

    public sealed record PeriodSnapshot
    {
        public required DayOfWeek Day { get; init; }
        public required TimeOnly Start { get; init; }
        public required TimeOnly End { get; init; }
        public required bool IsAllDay { get; init; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WeeklyScheduleTests.ScheduleSnapshot))]
internal partial class WeeklyScheduleJsonContext : JsonSerializerContext;
