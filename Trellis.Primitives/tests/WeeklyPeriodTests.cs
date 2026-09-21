namespace Trellis.Primitives.Tests;

using Trellis.Testing;

public class WeeklyPeriodTests
{
    [Theory]
    [InlineData(DayOfWeek.Sunday, 9, 17)]
    [InlineData(DayOfWeek.Monday, 22, 2)]
    [InlineData(DayOfWeek.Saturday, 23, 0)]
    public void TryCreate_ValidPeriod_PreservesComponents(DayOfWeek day, int start, int end)
    {
        var period = WeeklyPeriod.TryCreate(day, new TimeOnly(start, 0), new TimeOnly(end, 0))
            .Should().BeSuccess().Which;

        period.Day.Should().Be(day);
        period.Start.Should().Be(new TimeOnly(start, 0));
        period.End.Should().Be(new TimeOnly(end, 0));
        period.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public void TryCreate_SubsecondPeriod_PreservesEveryTick()
    {
        var start = new TimeOnly(123456789);
        var end = start.Add(TimeSpan.FromTicks(1));

        var period = WeeklyPeriod.TryCreate(DayOfWeek.Monday, start, end).Should().BeSuccess().Which;

        period.Start.Should().Be(start);
        period.End.Should().Be(end);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void TryCreate_UndefinedDay_ReportsAllowedMembers(int day)
    {
        var error = WeeklyPeriod.TryCreate((DayOfWeek)day, new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/day");
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.EnumUndefined);
        error.Fields[0].Args.Should().BeEquivalentTo(ValidationArgs.Allowed(Enum.GetNames<DayOfWeek>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(23)]
    public void TryCreate_EqualEndpoints_RequiresExplicitAllDay(int hour)
    {
        var time = new TimeOnly(hour, 0);
        var error = WeeklyPeriod.TryCreate(DayOfWeek.Monday, time, time)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields[0].Field.Path.Should().Be("/end");
        error.Fields[0].ReasonCode.Should().Be(ValidationCodes.ValueMustNotEqual);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("Opening", "/opening")]
    [InlineData("/periods/2", "/periods/2")]
    [InlineData("a/b~c", "/a~1b~0c")]
    public void TryCreate_InvalidComponents_AccumulatesNestedViolations(string? owner, string prefix)
    {
        var error = WeeklyPeriod.TryCreate((DayOfWeek)7, TimeOnly.MinValue, TimeOnly.MinValue, owner)
            .Should().BeFailureOfType<Error.InvalidInput>().Which;

        error.Fields.Items.Select(v => v.Field.Path).Should().Equal([$"{prefix}/day", $"{prefix}/end"]);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Saturday)]
    public void TryCreateAllDay_ValidDay_UsesExplicitMidnightEndpoints(DayOfWeek day)
    {
        var period = WeeklyPeriod.TryCreateAllDay(day).Should().BeSuccess().Which;

        period.Day.Should().Be(day);
        period.Start.Should().Be(TimeOnly.MinValue);
        period.End.Should().Be(TimeOnly.MinValue);
        period.IsAllDay.Should().BeTrue();
    }

    [Fact]
    public void TryCreateAllDay_UndefinedDay_Fails() =>
        WeeklyPeriod.TryCreateAllDay((DayOfWeek)7).Should().BeFailureOfType<Error.InvalidInput>();

    [Fact]
    public void Create_InvalidInput_ThrowsAtTrustedBoundary()
    {
        Action normal = () => WeeklyPeriod.Create(DayOfWeek.Monday, TimeOnly.MinValue, TimeOnly.MinValue);
        Action allDay = () => WeeklyPeriod.CreateAllDay((DayOfWeek)7);

        normal.Should().Throw<InvalidOperationException>()
            .WithMessage("Result<WeeklyPeriod> was a failure.*Start and end must differ.*");
        allDay.Should().Throw<InvalidOperationException>()
            .WithMessage("Result<WeeklyPeriod> was a failure.*Day must be a defined day of the week.*");
    }

    [Fact]
    public void Equality_SameComponents_UsesInheritedValueSemantics()
    {
        var first = WeeklyPeriod.Create(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0));
        var second = WeeklyPeriod.Create(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0));

        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.CompareTo(second).Should().Be(0);
        (first == WeeklyPeriod.CreateAllDay(DayOfWeek.Monday)).Should().BeFalse();
    }
}
