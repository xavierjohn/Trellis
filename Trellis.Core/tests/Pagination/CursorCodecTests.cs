namespace Trellis.Core.Tests.Pagination;

using System;
using System.Globalization;

/// <summary>
/// Unit tests for the <see cref="CursorCodec"/> helpers that encode and decode opaque
/// <see cref="Cursor"/> tokens for single-key and composite <c>(CreatedAt, Id)</c>
/// pagination.
/// </summary>
public class CursorCodecTests
{
    // ───── Single-key encode/decode ────────────────────────────────────────────

    [Fact]
    public void Single_key_round_trips_guid()
    {
        var id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        var cursor = CursorCodec.Encode(id);
        var decoded = CursorCodec.TryDecode<Guid>(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var parsed).Should().BeTrue();
        parsed.Should().Be(id);
    }

    [Fact]
    public void Single_key_round_trips_long()
    {
        var cursor = CursorCodec.Encode(42L);
        var decoded = CursorCodec.TryDecode<long>(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var parsed).Should().BeTrue();
        parsed.Should().Be(42L);
    }

    [Fact]
    public void Single_key_round_trips_int()
    {
        var cursor = CursorCodec.Encode(1234);
        var decoded = CursorCodec.TryDecode<int>(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var parsed).Should().BeTrue();
        parsed.Should().Be(1234);
    }

    [Fact]
    public void Single_key_fails_on_malformed_base64()
    {
        var bogus = new Cursor("!!!not base64!!!");

        var decoded = CursorCodec.TryDecode<Guid>(bogus);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void TryDecode_OversizedToken_FailsQuicklyWithMalformedError()
    {
        var oversized = new string('A', 4096);
        var cursor = new Cursor(oversized);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var decoded = CursorCodec.TryDecode<string>(cursor, fieldName: "cursor");
        sw.Stop();

        decoded.IsFailure.Should().BeTrue();
        var invalid = decoded.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        invalid.Fields.Length.Should().Be(1);
        invalid.Fields[0].ReasonCode.Should().Be("cursor.malformed");
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public void Single_key_fails_when_payload_unparseable_for_target_key()
    {
        var bogus = CursorCodec.Encode("not-a-guid");

        var decoded = CursorCodec.TryDecode<Guid>(bogus);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Single_key_fails_when_payload_bytes_are_invalid_utf8()
    {
        // 0xC0 0x80 is overlong-encoded NUL — a classic invalid UTF-8 sequence
        // that lenient decoders silently turn into replacement characters.
        // We want the codec to reject such cursors instead of admitting them
        // as the literal string "��".
        var standard = Convert.ToBase64String(new byte[] { 0xC0, 0x80 });
        var urlSafe = standard.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var bogus = new Cursor(urlSafe);

        var decoded = CursorCodec.TryDecode<string>(bogus);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Single_key_propagates_field_name_into_error()
    {
        var bogus = new Cursor("!!!malformed!!!");

        var decoded = CursorCodec.TryDecode<Guid>(bogus, fieldName: "after");

        decoded.IsFailure.Should().BeTrue();
        var invalid = decoded.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        invalid.Fields.Items.Should().Contain(f => f.Field.ToString().Contains("after"));
    }

    [Fact]
    public void Single_key_encode_rejects_non_formattable_non_string_key()
    {
        // A custom type that is neither IFormattable nor string. Without an invariant-culture
        // formatting contract, encoding could emit a culture-sensitive token that decode then
        // fails to parse — we want a clear, immediate NotSupportedException instead.
        var key = new NonFormattableKey();

        var act = () => CursorCodec.Encode(key);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Single_key_rejects_empty_string_id()
    {
        // FormatInvariant("") yields an empty payload, which would in turn cause
        // the Cursor ctor to throw with a confusing "token" message. The codec
        // should reject the input up front against the right parameter name.
        var act = () => CursorCodec.Encode("");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("id");
    }

    [Fact]
    public void Single_key_rejects_default_cursor_without_throwing()
    {
        // default(Cursor) bypasses the validated constructor; reading Token throws
        // InvalidOperationException. The Try* contract says no exceptions reach the
        // caller, so the codec must surface this as Error.InvalidInput.
        var decoded = CursorCodec.TryDecode<Guid>(default);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    private sealed class NonFormattableKey
    {
        public override string ToString() => "anything";
    }

    // ───── Composite (CreatedAt, Id) encode/decode ─────────────────────────────

    [Fact]
    public void Composite_rejects_empty_string_id()
    {
        // Symmetric with single-key Encode("") — composite must not silently produce
        // a "{date}|" payload that round-trips to an unparseable id for most TKey.
        var act = () => CursorCodec.Encode(DateTimeOffset.UtcNow, "");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("id");
    }

    [Fact]
    public void Composite_round_trips_datetimeoffset_and_guid()
    {
        var createdAt = new DateTimeOffset(2026, 5, 22, 14, 30, 12, TimeSpan.FromHours(-7))
            .AddTicks(1234567);
        var id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        var cursor = CursorCodec.Encode(createdAt, id);
        var decoded = CursorCodec.TryDecodeComposite<Guid>(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var pair).Should().BeTrue();
        pair.CreatedAt.Should().Be(createdAt);
        pair.Id.Should().Be(id);
    }

    [Fact]
    public void Composite_uses_invariant_culture_across_thread_culture_change()
    {
        // Verifies the codec is culture-neutral. fr-FR formats decimals with comma rather
        // than dot; if the codec accidentally used the current culture, the round-trip
        // would parse the dot-formatted ticks back incorrectly.
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var createdAt = new DateTimeOffset(2026, 5, 22, 14, 30, 12, TimeSpan.FromHours(-7))
                .AddTicks(1234567);
            var id = Guid.NewGuid();

            var cursor = CursorCodec.Encode(createdAt, id);
            var decoded = CursorCodec.TryDecodeComposite<Guid>(cursor);

            decoded.IsSuccess.Should().BeTrue();
            decoded.TryGetValue(out var pair).Should().BeTrue();
            pair.CreatedAt.Should().Be(createdAt);
            pair.Id.Should().Be(id);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Composite_fails_on_malformed_base64()
    {
        var bogus = new Cursor("not base64 at all");

        var decoded = CursorCodec.TryDecodeComposite<Guid>(bogus);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Composite_fails_when_separator_missing()
    {
        // Encode something that is valid base64url but contains no '|' delimiter.
        var noPipe = CursorCodec.Encode("2026-05-22T14:30:12Z and the id"); // single-key form, no pipe

        var decoded = CursorCodec.TryDecodeComposite<Guid>(noPipe);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Composite_fails_when_date_unparseable()
    {
        // Manually craft a bad composite payload: bogus date, valid guid.
        var raw = "not-a-date|550e8400-e29b-41d4-a716-446655440000";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var cursor = new Cursor(b64);

        var decoded = CursorCodec.TryDecodeComposite<Guid>(cursor);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Composite_fails_when_key_unparseable_for_target_type()
    {
        var raw = "2026-05-22T14:30:12.1234567-07:00|not-a-guid";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var cursor = new Cursor(b64);

        var decoded = CursorCodec.TryDecodeComposite<Guid>(cursor);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Composite_splits_at_first_pipe_only()
    {
        // Defensive: even if a hypothetical TKey serialized with embedded '|', the date
        // segment is unambiguous because we split at the FIRST '|' only. Here we encode
        // (date, "abc|def") and decode as string to verify the split semantics.
        var createdAt = new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero);
        var id = "abc|def";

        var cursor = CursorCodec.Encode(createdAt, id);
        var decoded = CursorCodec.TryDecodeComposite<string>(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var pair).Should().BeTrue();
        pair.CreatedAt.Should().Be(createdAt);
        pair.Id.Should().Be("abc|def");
    }

    [Fact]
    public void Composite_rejects_default_cursor_without_throwing()
    {
        // Mirror of Single_key_rejects_default_cursor_without_throwing for the
        // composite overload — the Try* contract must never let a default(Cursor)
        // turn into an InvalidOperationException from Cursor.Token.
        var decoded = CursorCodec.TryDecodeComposite<Guid>(default);

        decoded.IsFailure.Should().BeTrue();
        decoded.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void Composite_propagates_field_name_into_error()
    {
        var bogus = new Cursor("!!!malformed!!!");

        var decoded = CursorCodec.TryDecodeComposite<Guid>(bogus, fieldName: "after");

        decoded.IsFailure.Should().BeTrue();
        var invalid = decoded.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        invalid.Fields.Items.Should().Contain(f => f.Field.ToString().Contains("after"));
    }

    [Fact]
    public void Encoded_cursor_token_is_url_safe_base64()
    {
        var id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var cursor = CursorCodec.Encode(id);

        // URL-safe base64: '-' and '_' instead of '+' and '/', no padding.
        cursor.Token.Should().NotContain("+");
        cursor.Token.Should().NotContain("/");
        cursor.Token.Should().NotContain("=");
    }
}