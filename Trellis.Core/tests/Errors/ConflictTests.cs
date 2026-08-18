namespace Trellis.Core.Tests.Errors;

using System.Text.Json;
using Trellis.Testing;

public sealed class ConflictTests
{
    [Fact]
    public void Kind_is_conflict()
    {
        var error = new Error.Conflict(null, "duplicate.key");

        error.Kind.Should().Be("conflict");
    }

    [Fact]
    public void Code_returns_reason_code()
    {
        var error = new Error.Conflict(null, "duplicate.key");

        error.Code.Should().Be("duplicate.key");
    }

    [Fact]
    public void ConstraintName_and_ConstraintTableName_default_to_null()
    {
        var error = new Error.Conflict(null, "duplicate.key");

        error.ConstraintName.Should().BeNull();
        error.ConstraintTableName.Should().BeNull();
    }

    [Fact]
    public void ConstraintName_and_ConstraintTableName_round_trip_through_object_initializer()
    {
        var error = new Error.Conflict(null, "duplicate.key")
        {
            Detail = "A record with the same unique value already exists.",
            ConstraintName = "IX_Probes_Url",
            ConstraintTableName = "dbo.Probes",
        };

        error.ConstraintName.Should().Be("IX_Probes_Url");
        error.ConstraintTableName.Should().Be("dbo.Probes");
    }

    [Fact]
    public void Two_conflicts_with_same_payload_and_null_constraint_fields_are_equal()
    {
        var left = new Error.Conflict(null, "duplicate.key") { Detail = "msg" };
        var right = new Error.Conflict(null, "duplicate.key") { Detail = "msg" };

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Two_conflicts_differing_only_in_ConstraintName_are_not_equal()
    {
        var left = new Error.Conflict(null, "duplicate.key") { ConstraintName = "IX_A" };
        var right = new Error.Conflict(null, "duplicate.key") { ConstraintName = "IX_B" };

        left.Equals(right).Should().BeFalse();
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }

    [Fact]
    public void Two_conflicts_differing_only_in_ConstraintTableName_are_not_equal()
    {
        var left = new Error.Conflict(null, "duplicate.key") { ConstraintTableName = "dbo.Probes" };
        var right = new Error.Conflict(null, "duplicate.key") { ConstraintTableName = "dbo.Subscriptions" };

        left.Equals(right).Should().BeFalse();
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }

    [Fact]
    public void ConstraintName_and_ConstraintTableName_are_excluded_from_JSON_serialization()
    {
        // Constraint identity is telemetry-only — it can leak schema details and must
        // never appear in API responses produced by default System.Text.Json serialization.
        var error = new Error.Conflict(null, "duplicate.key")
        {
            Detail = "A record with the same unique value already exists.",
            ConstraintName = "IX_Probes_Url",
            ConstraintTableName = "dbo.Probes",
        };

        var json = JsonSerializer.Serialize(error);

        json.Should().NotContain("IX_Probes_Url");
        json.Should().NotContain("dbo.Probes");
        json.Should().NotContain("ConstraintName");
        json.Should().NotContain("ConstraintTableName");
    }
}