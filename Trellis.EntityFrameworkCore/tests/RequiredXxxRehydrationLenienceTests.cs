namespace Trellis.EntityFrameworkCore.Tests;

using System;
using Trellis;
using Xunit;

// Both fixtures are now bare lenient under the post-flip defaults; the test names retain
// their historical "Lenient" / "Strict" labels but the fixtures and assertions all express
// the lenient behavior (no rejection of Guid.Empty / DateTime.MinValue / "").
public partial class LenientEfGuid : RequiredGuid<LenientEfGuid> { }

public partial class StrictEfGuid : RequiredGuid<StrictEfGuid> { }

public partial class LenientEfDateTime : RequiredDateTime<LenientEfDateTime> { }

public partial class StrictEfDateTime : RequiredDateTime<StrictEfDateTime> { }

public partial class LenientEfString : RequiredString<LenientEfString> { }

public partial class StrictEfString : RequiredString<StrictEfString> { }

/// <summary>
/// Regression coverage for the EF read-path impact: <see cref="TrellisScalarConverter{TModel, TProvider}"/>
/// calls <c>TryCreate</c> to materialize every row, so the bare Required* base materializes legacy
/// column sentinels (<c>Guid.Empty</c>, <c>DateTime.MinValue</c>, <c>""</c>) without throwing
/// <c>TrellisPersistenceMappingException</c>.
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
    public void StrictGuidConverter_materializes_Guid_Empty_without_throwing()
    {
        var converter = new TrellisScalarConverter<StrictEfGuid, Guid>();
        var materialized = (StrictEfGuid)converter.ConvertFromProvider(Guid.Empty)!;
        materialized.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void LenientDateTimeConverter_materializes_MinValue_without_throwing()
    {
        var converter = new TrellisScalarConverter<LenientEfDateTime, DateTime>();
        var materialized = (LenientEfDateTime)converter.ConvertFromProvider(DateTime.MinValue)!;
        materialized.Value.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void StrictDateTimeConverter_materializes_MinValue_without_throwing()
    {
        var converter = new TrellisScalarConverter<StrictEfDateTime, DateTime>();
        var materialized = (StrictEfDateTime)converter.ConvertFromProvider(DateTime.MinValue)!;
        materialized.Value.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void LenientStringConverter_materializes_empty_string_without_throwing()
    {
        var converter = new TrellisScalarConverter<LenientEfString, string>();
        var materialized = (LenientEfString)converter.ConvertFromProvider("")!;
        materialized.Value.Should().Be("");
    }

    [Fact]
    public void StrictStringConverter_materializes_empty_string_without_throwing()
    {
        var converter = new TrellisScalarConverter<StrictEfString, string>();
        var materialized = (StrictEfString)converter.ConvertFromProvider("")!;
        materialized.Value.Should().Be("");
    }
}