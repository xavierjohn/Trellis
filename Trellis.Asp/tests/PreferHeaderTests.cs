namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Tests for <see cref="PreferHeader"/> — RFC 7240 Prefer request header parsing.
/// </summary>
public class PreferHeaderTests
{
    private static HttpRequest CreateRequest(string? preferHeaderValue = null)
    {
        var context = new DefaultHttpContext();
        if (preferHeaderValue is not null)
            context.Request.Headers["Prefer"] = preferHeaderValue;
        return context.Request;
    }

    #region Parse — Missing / Empty Header

    [Fact]
    public void Parse_NoPreferHeader_ReturnsEmptyPreferences()
    {
        var request = CreateRequest();

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeFalse();
        prefer.ReturnMinimal.Should().BeFalse();
        prefer.RespondAsync.Should().BeFalse();
        prefer.Wait.Should().BeNull();
        prefer.HandlingStrict.Should().BeFalse();
        prefer.HandlingLenient.Should().BeFalse();
        prefer.HasPreferences.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyPreferHeader_ReturnsEmptyPreferences()
    {
        var request = CreateRequest("");

        var prefer = PreferHeader.Parse(request);

        prefer.HasPreferences.Should().BeFalse();
    }

    #endregion

    #region Parse — return=representation / return=minimal

    [Fact]
    public void Parse_ReturnRepresentation_SetsFlag()
    {
        var request = CreateRequest("return=representation");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeTrue();
        prefer.ReturnMinimal.Should().BeFalse();
        prefer.HasPreferences.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReturnMinimal_SetsFlag()
    {
        var request = CreateRequest("return=minimal");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeTrue();
        prefer.ReturnRepresentation.Should().BeFalse();
        prefer.HasPreferences.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReturnRepresentation_CaseInsensitiveTokenName_CaseSensitiveValue()
    {
        // RFC 7240 §2: token names are case-insensitive, values are case-sensitive
        var request = CreateRequest("Return=representation");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReturnMinimal_CaseInsensitiveTokenName_CaseSensitiveValue()
    {
        var request = CreateRequest("RETURN=minimal");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReturnRepresentation_WrongCase_Value_Ignored()
    {
        // RFC 7240 §2: values are case-sensitive — "Representation" != "representation"
        var request = CreateRequest("return=Representation");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeFalse();
        prefer.ReturnMinimal.Should().BeFalse();
    }

    [Fact]
    public void Parse_ReturnMinimal_WrongCase_Value_Ignored()
    {
        var request = CreateRequest("return=MINIMAL");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeFalse();
    }

    #endregion

    #region Parse — respond-async

    [Fact]
    public void Parse_RespondAsync_SetsFlag()
    {
        var request = CreateRequest("respond-async");

        var prefer = PreferHeader.Parse(request);

        prefer.RespondAsync.Should().BeTrue();
        prefer.HasPreferences.Should().BeTrue();
    }

    [Fact]
    public void Parse_RespondAsync_CaseInsensitive()
    {
        var request = CreateRequest("Respond-Async");

        var prefer = PreferHeader.Parse(request);

        prefer.RespondAsync.Should().BeTrue();
    }

    #endregion

    #region Parse — wait=N

    [Fact]
    public void Parse_Wait_ParsesSeconds()
    {
        var request = CreateRequest("wait=100");

        var prefer = PreferHeader.Parse(request);

        prefer.Wait.Should().Be(100);
        prefer.HasPreferences.Should().BeTrue();
    }

    [Fact]
    public void Parse_Wait_Zero_ParsesAsZero()
    {
        var request = CreateRequest("wait=0");

        var prefer = PreferHeader.Parse(request);

        prefer.Wait.Should().Be(0);
    }

    [Fact]
    public void Parse_Wait_NonNumeric_IgnoresPreference()
    {
        var request = CreateRequest("wait=abc");

        var prefer = PreferHeader.Parse(request);

        prefer.Wait.Should().BeNull();
    }

    #endregion

    #region Parse — handling=strict / handling=lenient

    [Fact]
    public void Parse_HandlingStrict_SetsFlag()
    {
        var request = CreateRequest("handling=strict");

        var prefer = PreferHeader.Parse(request);

        prefer.HandlingStrict.Should().BeTrue();
        prefer.HandlingLenient.Should().BeFalse();
    }

    [Fact]
    public void Parse_HandlingLenient_SetsFlag()
    {
        var request = CreateRequest("handling=lenient");

        var prefer = PreferHeader.Parse(request);

        prefer.HandlingLenient.Should().BeTrue();
        prefer.HandlingStrict.Should().BeFalse();
    }

    #endregion

    #region Parse — Multiple Preferences

    [Fact]
    public void Parse_MultiplePreferences_CommaDelimited_ParsesAll()
    {
        var request = CreateRequest("respond-async, wait=100");

        var prefer = PreferHeader.Parse(request);

        prefer.RespondAsync.Should().BeTrue();
        prefer.Wait.Should().Be(100);
    }

    [Fact]
    public void Parse_MultiplePreferences_AllStandard()
    {
        var request = CreateRequest("return=representation, respond-async, wait=30, handling=lenient");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeTrue();
        prefer.RespondAsync.Should().BeTrue();
        prefer.Wait.Should().Be(30);
        prefer.HandlingLenient.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultipleHeaderValues_ParsesBoth()
    {
        // RFC 7240 §2: Multiple Prefer header fields are equivalent to comma-separated
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Prefer", "respond-async");
        context.Request.Headers.Append("Prefer", "return=minimal");

        var prefer = PreferHeader.Parse(context.Request);

        prefer.RespondAsync.Should().BeTrue();
        prefer.ReturnMinimal.Should().BeTrue();
    }

    #endregion

    #region Parse — Unknown Preferences Ignored

    [Fact]
    public void Parse_UnknownPreference_IgnoredWithoutError()
    {
        var request = CreateRequest("unknown-preference, return=representation");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeTrue();
    }

    [Fact]
    public void Parse_PreferenceWithParameters_ParametersIgnored()
    {
        // RFC 7240 §2: parameters are preference-specific; unknown ones are ignored
        var request = CreateRequest("return=minimal; foo=\"bar\"");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeTrue();
    }

    #endregion

    #region Parse — Duplicate Preferences (First Wins)

    [Fact]
    public void Parse_DuplicateReturn_FirstWins()
    {
        // RFC 7240 §2: "If any preference is specified more than once, only the first instance is to be considered"
        var request = CreateRequest("return=minimal, return=representation");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeTrue();
        prefer.ReturnRepresentation.Should().BeFalse();
    }

    #endregion

    #region Parse — Whitespace Handling

    [Fact]
    public void Parse_ExtraWhitespace_ParsesCorrectly()
    {
        var request = CreateRequest("  return=representation ,  wait=50  ");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnRepresentation.Should().BeTrue();
        prefer.Wait.Should().Be(50);
    }

    [Fact]
    public void Parse_BWS_AroundEquals_ParsesCorrectly()
    {
        // RFC 7240 §2: preference = token [ BWS "=" BWS word ]
        var request = CreateRequest("return = minimal");

        var prefer = PreferHeader.Parse(request);

        prefer.ReturnMinimal.Should().BeTrue();
    }

    [Fact]
    public void Parse_BWS_Wait_ParsesCorrectly()
    {
        var request = CreateRequest("wait = 30");

        var prefer = PreferHeader.Parse(request);

        prefer.Wait.Should().Be(30);
    }

    [Fact]
    public void Parse_BWS_Handling_ParsesCorrectly()
    {
        var request = CreateRequest("handling = strict");

        var prefer = PreferHeader.Parse(request);

        prefer.HandlingStrict.Should().BeTrue();
    }

    #endregion

    #region Parse — Duplicate wait (First Wins)

    [Fact]
    public void Parse_DuplicateWait_FirstWins()
    {
        // RFC 7240 §2: first occurrence wins for all preferences
        var request = CreateRequest("wait=10, wait=1");

        var prefer = PreferHeader.Parse(request);

        prefer.Wait.Should().Be(10);
    }

    #endregion
}
