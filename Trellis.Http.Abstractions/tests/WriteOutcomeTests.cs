namespace Trellis.Http.Abstractions.Tests;

using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="WriteOutcome"/> factory helpers. The defining behavior is that each
/// helper returns the <em>base</em> <see cref="WriteOutcome{T}"/> (not the nested case), so the
/// results assign to a <c>WriteOutcome&lt;Doc&gt;</c> local with no cast and flow through generic
/// pipelines that bind on <c>Result&lt;WriteOutcome&lt;T&gt;&gt;</c>.
/// </summary>
public class WriteOutcomeTests
{
    private sealed record Doc(int Id);

    [Fact]
    public void Created_returns_base_typed_case_with_payload()
    {
        var doc = new Doc(1);
        var meta = RepresentationMetadata.WithStrongETag("etag-c");

        WriteOutcome<Doc> outcome = WriteOutcome.Created(doc, "/docs/1", meta);

        var created = outcome.Should().BeOfType<WriteOutcome<Doc>.Created>().Subject;
        created.Value.Should().Be(doc);
        created.Location.Should().Be("/docs/1");
        created.Metadata.Should().Be(meta);
    }

    [Fact]
    public void Updated_returns_base_typed_case_with_payload()
    {
        var doc = new Doc(2);
        var meta = RepresentationMetadata.WithStrongETag("etag-u");

        WriteOutcome<Doc> outcome = WriteOutcome.Updated(doc, meta);

        var updated = outcome.Should().BeOfType<WriteOutcome<Doc>.Updated>().Subject;
        updated.Value.Should().Be(doc);
        updated.Metadata.Should().Be(meta);
    }

    [Fact]
    public void Updated_metadata_is_optional()
    {
        WriteOutcome<Doc> outcome = WriteOutcome.Updated(new Doc(3));

        outcome.Should().BeOfType<WriteOutcome<Doc>.Updated>()
            .Which.Metadata.Should().BeNull();
    }

    [Fact]
    public void UpdatedNoContent_returns_base_typed_case_with_metadata()
    {
        var meta = RepresentationMetadata.WithStrongETag("etag-unc");

        WriteOutcome<Doc> outcome = WriteOutcome.UpdatedNoContent<Doc>(meta);

        outcome.Should().BeOfType<WriteOutcome<Doc>.UpdatedNoContent>()
            .Which.Metadata.Should().Be(meta);
    }

    [Fact]
    public void Accepted_returns_base_typed_case_with_payload()
    {
        var doc = new Doc(4);
        var retry = RetryAfterValue.FromSeconds(30);

        WriteOutcome<Doc> outcome = WriteOutcome.Accepted(doc, "/status/4", retry);

        var accepted = outcome.Should().BeOfType<WriteOutcome<Doc>.Accepted>().Subject;
        accepted.StatusBody.Should().Be(doc);
        accepted.MonitorUri.Should().Be("/status/4");
        accepted.RetryAfter.Should().Be(retry);
    }

    [Fact]
    public void AcceptedNoContent_returns_base_typed_case()
    {
        WriteOutcome<Doc> outcome = WriteOutcome.AcceptedNoContent<Doc>("/status/5");

        outcome.Should().BeOfType<WriteOutcome<Doc>.AcceptedNoContent>()
            .Which.MonitorUri.Should().Be("/status/5");
    }
}