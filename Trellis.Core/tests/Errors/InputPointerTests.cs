namespace Trellis.Core.Tests.Errors;

/// <summary>
/// Tests for <see cref="InputPointer"/>'s RFC 6901 (JSON Pointer) compliance.
/// RFC 6901 §3 requires that the special characters '~' and '/' inside a property name
/// be escaped as "~0" and "~1" respectively (and the order matters: '~' must be escaped
/// FIRST, otherwise '/' → '~1' would be re-escaped as '~01').
/// </summary>
public class InputPointerTests
{
    [Fact]
    public void ForProperty_with_simple_name_prepends_slash() =>
        InputPointer.ForProperty("Email").Path.Should().Be("/Email");

    [Fact]
    public void ForProperty_with_empty_returns_root() =>
        InputPointer.ForProperty("").Should().Be(InputPointer.Root);

    [Fact]
    public void ForProperty_with_null_returns_root() =>
        InputPointer.ForProperty(null!).Should().Be(InputPointer.Root);

    [Fact]
    public void ForProperty_with_existing_pointer_does_not_double_escape() =>
        // Caller passing a fully-formed pointer (e.g. from JsonPointerNormalizer) must be preserved.
        InputPointer.ForProperty("/Lines/0/Memo").Path.Should().Be("/Lines/0/Memo");

    [Fact]
    public void ForProperty_escapes_tilde_per_rfc_6901() =>
        // RFC 6901 §3: '~' must be escaped as '~0' so the pointer can roundtrip.
        InputPointer.ForProperty("data~field").Path.Should().Be("/data~0field");

    [Fact]
    public void ForProperty_escapes_slash_in_property_name_per_rfc_6901() =>
        // RFC 6901 §3: '/' inside a property name must be escaped as '~1'.
        // Otherwise "email/work" would parse back as a nested pointer (/email then /work)
        // rather than a single property literally named "email/work".
        InputPointer.ForProperty("email/work").Path.Should().Be("/email~1work");

    [Fact]
    public void ForProperty_escapes_tilde_before_slash_per_rfc_6901_order() =>
        // RFC 6901 §3 mandates the escape order: '~' first, then '/'. Otherwise
        // '/' → '~1' would be re-escaped as '~01' on the second pass.
        // Input: "~/" should produce "/~0~1", NOT "/~01".
        InputPointer.ForProperty("~/").Path.Should().Be("/~0~1");
}