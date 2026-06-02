namespace Trellis.Primitives.Tests;

using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Trellis.Testing;
using Xunit;

public partial class EmployeeId : RequiredGuid<EmployeeId>
{
}

public class RequiredGuidTests
{
    [Fact]
    public void Cannot_create_empty_RequiredGuid()
    {
        var guidId1 = EmployeeId.TryCreate(default(Guid));
        guidId1.IsFailure.Should().BeTrue();
        guidId1.UnwrapError().Should().BeOfType<Error.InvalidInput>();
        var validation = (Error.InvalidInput)guidId1.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/employeeId");
        validation.Fields[0].Detail.Should().Be("Employee Id cannot be Guid.Empty.");
        validation.Fields[0].ReasonCode.Should().Be("validation.error");
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = EmployeeId.TryCreate(default(Guid), "myField");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/myField");
    }

    [Fact]
    public void Can_create_RequiredGuid_from_Guid()
    {
        var guid = Guid.NewGuid();
        EmployeeId.TryCreate(guid)
            .Tap(empId =>
            {
                empId.Should().BeOfType<EmployeeId>();
                ((Guid)empId).Should().Be(guid);
            })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Can_create_RequiredGuid_from_valid_string()
    {
        // Arrange
        var strGuid = Guid.NewGuid().ToString();

        // Act
        EmployeeId.TryCreate(strGuid)
            .Tap(empId =>
            {
                empId.Should().BeOfType<EmployeeId>();
                empId.ToString(CultureInfo.InvariantCulture).Should().Be(strGuid);
            })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Two_RequiredGuid_with_different_values_should_not_be_equal() =>
        EmployeeId.TryCreate(Guid.NewGuid())
            .Combine(EmployeeId.TryCreate(Guid.NewGuid()))
            .Tap((emp1, emp2) =>
            {
                (emp1 != emp2).Should().BeTrue();
                emp1.Equals(emp2).Should().BeFalse();
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredGuid_with_same_value_should_be_equal()
    {
        var myGuid = Guid.NewGuid();
        EmployeeId.TryCreate(myGuid)
            .Combine(EmployeeId.TryCreate(myGuid))
            .Tap((emp1, emp2) =>
            {
                (emp1 == emp2).Should().BeTrue();
                emp1.Equals(emp2).Should().BeTrue();
                emp1.GetHashCode().Should().Be(emp2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Can_use_ToString()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var myGuid = EmployeeId.TryCreate(guid).Unwrap();

        // Act
        var actual = myGuid.ToString(CultureInfo.InvariantCulture);

        // Assert
        actual.Should().Be(guid.ToString());
    }

    [Fact]
    public void Can_implicitly_cast_to_guid()
    {
        // Arrange
        Guid myGuid = Guid.NewGuid();
        EmployeeId myGuidId1 = EmployeeId.TryCreate(myGuid).Unwrap();

        // Act
        Guid primGuid = myGuidId1;

        // Assert
        primGuid.Should().Be(myGuid);
    }

    [Fact]
    public void Can_cast_to_RequiredGuid()
    {
        // Arrange
        Guid myGuid = Guid.NewGuid();

        // Act
        EmployeeId myGuidId1 = (EmployeeId)myGuid;

        // Assert
        myGuidId1.Value.Should().Be(myGuid);
    }

    [Fact]
    public void Cannot_cast_empty_to_RequiredGuid()
    {
        // Arrange
        Guid myGuid = default;
        EmployeeId myGuidId1;

        // Act
        Action act = () => myGuidId1 = (EmployeeId)myGuid;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create EmployeeId:*Employee Id cannot be Guid.Empty*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invalid")]
    public void Cannot_create_RequiredGuid_from_invalid_string(string value)
    {
        // Act
        var myGuidResult = EmployeeId.TryCreate(value);

        // Assert
        myGuidResult.IsFailure.Should().BeTrue();
        myGuidResult.UnwrapError().Should().BeOfType<Error.InvalidInput>();
        Error.InvalidInput ve = (Error.InvalidInput)myGuidResult.UnwrapError();
        ve.Fields[0].Field.Path.Should().Be("/employeeId");
        ve.Fields[0].Detail.Should().Be("Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)");

    }

    [Fact]
    public void Cannot_create_RequiredGuid_from_null_string()
    {
        // Null string -> "cannot be empty" (the null-rejection message; consistent across the family).
        var myGuidResult = EmployeeId.TryCreate((string?)null);

        myGuidResult.IsFailure.Should().BeTrue();
        var ve = (Error.InvalidInput)myGuidResult.UnwrapError();
        ve.Fields[0].Field.Path.Should().Be("/employeeId");
        ve.Fields[0].Detail.Should().Be("Employee Id cannot be empty.");
    }

    [Fact]
    public void Cannot_create_RequiredGuid_from_all_zero_string()
    {
        // "00000000-..." parses successfully into Guid.Empty, then the strict default check fires
        // with the per-type "cannot be Guid.Empty." message — distinct from the null case above.
        var myGuidResult = EmployeeId.TryCreate("00000000-0000-0000-0000-000000000000");

        myGuidResult.IsFailure.Should().BeTrue();
        var ve = (Error.InvalidInput)myGuidResult.UnwrapError();
        ve.Fields[0].Field.Path.Should().Be("/employeeId");
        ve.Fields[0].Detail.Should().Be("Employee Id cannot be Guid.Empty.");
    }

    [Fact]
    public void Can_create_RequiredGuid_from_try_parsing_valid_string()
    {
        // Arrange
        var strGuid = Guid.NewGuid().ToString();

        // Act
        EmployeeId.TryParse(strGuid, null, out var myGuid)
            .Should().BeTrue();

        // Assert
        myGuid.Should().BeOfType<EmployeeId>();
        myGuid!.ToString(CultureInfo.InvariantCulture).Should().Be(strGuid);
    }

    [Fact]
    public void Cannot_create_RequiredGuid_from_try_parsing_invalid_string()
    {
        // Arrange
        var strGuid = "bad string";

        // Act
        EmployeeId.TryParse(strGuid, null, out var myGuid)
            .Should().BeFalse();

        // Assert
        myGuid.Should().BeNull();
    }

    [Fact]
    public void Can_create_RequiredGuid_from_parsing_valid_string()
    {
        // Arrange
        var strGuid = Guid.NewGuid().ToString();

        // Act
        var myGuid = EmployeeId.Parse(strGuid, null);

        // Assert
        myGuid.Should().BeOfType<EmployeeId>();
        myGuid.ToString(CultureInfo.InvariantCulture).Should().Be(strGuid);
    }

    [Fact]
    public void Cannot_create_RequiredGuid_from_parsing_invalid_string()
    {
        // Arrange
        var strGuid = "bad string";

        // Act
        Action act = () => EmployeeId.Parse(strGuid, null);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)");
    }

    [Fact]
    public void Can_use_Contains()
    {
        // Arrange
        var employeeId1 = EmployeeId.NewUniqueV4();
        var employeeId2 = EmployeeId.NewUniqueV4();
        IReadOnlyList<EmployeeId> employeeIds = new List<EmployeeId> { employeeId1, employeeId2 };

        // Act
        var actual = employeeIds.Contains(employeeId1);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void NewUniqueV7_creates_valid_version7_guid()
    {
        // Act
        var employeeId = EmployeeId.NewUniqueV7();

        // Assert
        employeeId.Should().BeOfType<EmployeeId>();
        employeeId.Value.Should().NotBe(Guid.Empty);

        // Version 7 GUIDs have version nibble = 7 (bits 48-51)
        var bytes = employeeId.Value.ToByteArray();
        var versionNibble = (bytes[7] >> 4) & 0x0F;
        versionNibble.Should().Be(7, "GUID should be Version 7");
    }

    [Fact]
    public void NewUniqueV7_TimeProvider_ClockAdvances_GuidsSortChronologically()
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Act
        var id1 = EmployeeId.NewUniqueV7(clock);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var id2 = EmployeeId.NewUniqueV7(clock);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var id3 = EmployeeId.NewUniqueV7(clock);

        // Assert
        var sorted = new[] { id3, id1, id2 }.OrderBy(x => x.Value).ToArray();
        sorted.Should().Equal([id1, id2, id3]);
    }

    [Fact]
    public void NewUniqueV7_creates_unique_values()
    {
        // Act
        var ids = Enumerable.Range(0, 100).Select(_ => EmployeeId.NewUniqueV7()).ToList();

        // Assert
        ids.Distinct().Count().Should().Be(100, "all generated IDs should be unique");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var employeeId = EmployeeId.NewUniqueV4();
        Guid primEmployeeId = employeeId.Value;
        var expected = JsonSerializer.Serialize(primEmployeeId);

        // Act
        var actual = JsonSerializer.Serialize(employeeId);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        Guid guid = Guid.NewGuid();
        var json = JsonSerializer.Serialize(guid);

        // Act
        EmployeeId actual = JsonSerializer.Deserialize<EmployeeId>(json)!;

        // Assert
        actual.Value.Should().Be(guid);
    }

    [Fact]
    public void Cannot_create_RequiredGuid_from_parsing_invalid_string_in_json()
    {
        // Arrange
        var strGuid = JsonSerializer.Serialize("bad guid");

        // Act
        Action act = () => JsonSerializer.Deserialize<EmployeeId>(strGuid);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)");
    }
}