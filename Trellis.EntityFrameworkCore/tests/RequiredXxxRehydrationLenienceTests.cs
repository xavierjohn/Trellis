namespace Trellis.EntityFrameworkCore.Tests;

using System;
using Trellis;
using Xunit;

// Lenient fixtures are bare (post-flip default): TryCreate accepts every concrete value and
// rejects only null, so the EF converter materializes legacy column sentinels (Guid.Empty,
// DateTime.MinValue, "") without throwing. Strict fixtures opt back into sentinel rejection via
// [NotDefault] (and [Trim, NotDefault] for strings), so the converter throws
// TrellisPersistenceMappingException when it reads a sentinel — this is the regression coverage
// for the strict EF read-path that the lenient flip would otherwise have silently dropped.
public partial class LenientEfGuid : RequiredGuid<LenientEfGuid> { }

[NotDefault] public partial class StrictEfGuid : RequiredGuid<StrictEfGuid> { }

public partial class LenientEfDateTime : RequiredDateTime<LenientEfDateTime> { }

[NotDefault] public partial class StrictEfDateTime : RequiredDateTime<StrictEfDateTime> { }

public partial class LenientEfString : RequiredString<LenientEfString> { }

[Trim, NotDefault] public partial class StrictEfString : RequiredString<StrictEfString> { }

/// <summary>
/// Regression coverage for the EF read-path impact: <see cref="TrellisScalarConverter{TModel, TProvider}"/>
/// calls <c>TryCreate</c> to materialize every row. A bare (lenient) Required* base materializes
/// legacy column sentinels (<c>Guid.Empty</c>, <c>DateTime.MinValue</c>, <c>""</c>) without
/// throwing, whereas a fixture that opts into sentinel rejection via <c>[NotDefault]</c>
/// (or <c>[Trim, NotDefault]</c> for strings) makes the converter throw
/// <c>TrellisPersistenceMappingException</c> when it reads that sentinel. Both paths are asserted
/// so the lenient flip cannot silently drop the strict read-path coverage.
/// </summary>
/// <remarks>
/// Exercises the converter directly via <c>ValueConverter&lt;,&gt;.ConvertFromProvider</c> rather
/// than spinning up a DbContext. The materialization path is the same in both cases — EF Core's
/// query pipeline invokes the same delegate on every row read — so the converter-level assertion
/// is equivalent to the round-trip check but does not require a backing database. This keeps the
/// test compatible with the SQL-Server-less CI environment.
/// </remarks>
public class RequiredXxxRehydrationLenienceTests
{
    [Fact]
    public void LenientGuidConverter_materializes_Guid_Empty_without_throwing()
    {
        var converter = new TrellisScalarConverter<LenientEfGuid, Guid>();
        var materialized = (LenientEfGuid)converter.ConvertFromProvider(Guid.Empty)!;
        materialized.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void StrictGuidConverter_throws_when_materializing_Guid_Empty()
    {
        var converter = new TrellisScalarConverter<StrictEfGuid, Guid>();
        var act = () => converter.ConvertFromProvider(Guid.Empty);
        act.Should().Throw<TrellisPersistenceMappingException>()
            .Which.PersistedValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void LenientDateTimeConverter_materializes_MinValue_without_throwing()
    {
        var converter = new TrellisScalarConverter<LenientEfDateTime, DateTime>();
        var materialized = (LenientEfDateTime)converter.ConvertFromProvider(DateTime.MinValue)!;
        materialized.Value.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void StrictDateTimeConverter_throws_when_materializing_MinValue()
    {
        var converter = new TrellisScalarConverter<StrictEfDateTime, DateTime>();
        var act = () => converter.ConvertFromProvider(DateTime.MinValue);
        act.Should().Throw<TrellisPersistenceMappingException>()
            .Which.PersistedValue.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void LenientStringConverter_materializes_empty_string_without_throwing()
    {
        var converter = new TrellisScalarConverter<LenientEfString, string>();
        var materialized = (LenientEfString)converter.ConvertFromProvider("")!;
        materialized.Value.Should().Be("");
    }

    [Fact]
    public void StrictStringConverter_throws_when_materializing_empty_string()
    {
        var converter = new TrellisScalarConverter<StrictEfString, string>();
        var act = () => converter.ConvertFromProvider("");
        act.Should().Throw<TrellisPersistenceMappingException>()
            .Which.PersistedValue.Should().Be("");
    }
}