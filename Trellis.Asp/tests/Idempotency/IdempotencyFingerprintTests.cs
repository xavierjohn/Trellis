namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http;
using Trellis.Asp.Idempotency;

/// <summary>
/// Pins the contract of <see cref="IdempotencyFingerprint"/>: deterministic, order-independent
/// canonicalization of the HTTP request components that matter for replay safety, hashed via
/// SHA-256 (URL-safe base64). Two identical requests must produce the same fingerprint;
/// changing any captured component must produce a different fingerprint.
/// </summary>
public sealed class IdempotencyFingerprintTests
{
    private static DefaultHttpContext BuildContext(
        string method = "POST",
        string path = "/orders",
        string queryString = "",
        byte[]? body = null,
        string contentType = "application/json",
        string? contentEncoding = null,
        Dictionary<string, string>? extraHeaders = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(queryString);
        if (body is not null)
        {
            ctx.Request.Body = new MemoryStream(body);
        }

        ctx.Request.ContentType = contentType;
        if (contentEncoding is not null)
        {
            ctx.Request.Headers["Content-Encoding"] = contentEncoding;
        }

        if (extraHeaders is not null)
        {
            foreach (var kv in extraHeaders)
            {
                ctx.Request.Headers[kv.Key] = kv.Value;
            }
        }

        return ctx;
    }

    [Fact]
    public void Identical_requests_produce_identical_fingerprints()
    {
        var body = Encoding.UTF8.GetBytes("{\"x\":1}");
        var a = IdempotencyFingerprint.Compute(BuildContext(body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(body: body), body, new IdempotencyOptions());
        a.Should().Be(b);
    }

    [Fact]
    public void Method_difference_changes_fingerprint()
    {
        var body = new byte[] { 1, 2, 3 };
        var a = IdempotencyFingerprint.Compute(BuildContext(method: "POST", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(method: "PATCH", body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void Path_difference_changes_fingerprint()
    {
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(path: "/a", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(path: "/b", body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void PathBase_difference_changes_fingerprint()
    {
        // Two sub-apps mounted under different PathBase values can present the same
        // Request.Path. The fingerprint must include PathBase so the replay identity
        // matches the full request target and bodies cannot collide across branches.
        var body = new byte[] { 1 };
        var ctxA = BuildContext(path: "/orders", body: body);
        ctxA.Request.PathBase = "/v1";
        var ctxB = BuildContext(path: "/orders", body: body);
        ctxB.Request.PathBase = "/v2";
        var a = IdempotencyFingerprint.Compute(ctxA, body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(ctxB, body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void Query_order_does_not_change_fingerprint()
    {
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(queryString: "?a=1&b=2", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(queryString: "?b=2&a=1", body: body), body, new IdempotencyOptions());
        a.Should().Be(b, "query keys are sorted in the canonical form");
    }

    [Fact]
    public void Repeated_query_values_preserve_order_within_a_key()
    {
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(queryString: "?tag=red&tag=blue", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(queryString: "?tag=blue&tag=red", body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b, "repeated values within a single key are order-sensitive");
    }

    [Fact]
    public void Body_difference_changes_fingerprint()
    {
        var body1 = new byte[] { 1, 2 };
        var body2 = new byte[] { 1, 3 };
        var a = IdempotencyFingerprint.Compute(BuildContext(body: body1), body1, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(body: body2), body2, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void ContentType_difference_changes_fingerprint()
    {
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(contentType: "application/json", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(contentType: "application/xml", body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void ContentEncoding_difference_changes_fingerprint()
    {
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(contentEncoding: "gzip", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(contentEncoding: null, body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void Extra_headers_when_configured_change_fingerprint()
    {
        var body = new byte[] { 1 };
        var options = new IdempotencyOptions();
        options.AdditionalFingerprintHeaders.Add("X-Tenant");
        var a = IdempotencyFingerprint.Compute(BuildContext(body: body, extraHeaders: new() { ["X-Tenant"] = "acme" }), body, options);
        var b = IdempotencyFingerprint.Compute(BuildContext(body: body, extraHeaders: new() { ["X-Tenant"] = "globex" }), body, options);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Extra_headers_not_configured_do_not_change_fingerprint()
    {
        var body = new byte[] { 1 };
        var options = new IdempotencyOptions();
        var a = IdempotencyFingerprint.Compute(BuildContext(body: body, extraHeaders: new() { ["X-Tenant"] = "acme" }), body, options);
        var b = IdempotencyFingerprint.Compute(BuildContext(body: body, extraHeaders: new() { ["X-Tenant"] = "globex" }), body, options);
        a.Should().Be(b);
    }

    [Fact]
    public void Distinct_query_strings_that_aligned_under_unframed_separators_must_not_collide()
    {
        // Without per-field length-framing, the canonical query "?a=&a=b&c=" and "?a=&b=c&b="
        // both serialize to the same byte sequence (a 0x1F 0x1F b 0x1F c 0x1F) because the
        // separator-only form cannot tell whether the empty token belongs to key "a" or to
        // the boundary between two keys. They MUST hash differently — otherwise an attacker
        // (or a buggy client) could replay the wrong cached response for a retry whose query
        // changed.
        var body = new byte[] { 1 };
        var a = IdempotencyFingerprint.Compute(BuildContext(queryString: "?a=&a=b&c=", body: body), body, new IdempotencyOptions());
        var b = IdempotencyFingerprint.Compute(BuildContext(queryString: "?a=&b=c&b=", body: body), body, new IdempotencyOptions());
        a.Should().NotBe(b);
    }

    [Fact]
    public void Configured_header_missing_vs_empty_value_must_not_collide()
    {
        // Without an explicit presence marker, a configured header that is absent serializes
        // to the same bytes as the same header present with a single empty value (both
        // collapse to the header name + separator). Two retries that differ only in whether
        // the header was sent or sent-with-empty-value would replay each other's response.
        var body = new byte[] { 1 };
        var options = new IdempotencyOptions();
        options.AdditionalFingerprintHeaders.Add("X-Tenant");
        var absent = IdempotencyFingerprint.Compute(BuildContext(body: body), body, options);
        var empty = IdempotencyFingerprint.Compute(BuildContext(body: body, extraHeaders: new() { ["X-Tenant"] = "" }), body, options);
        absent.Should().NotBe(empty);
    }
}