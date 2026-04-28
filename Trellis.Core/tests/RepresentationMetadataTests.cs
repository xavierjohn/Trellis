namespace Trellis.Core.Tests;

public class RepresentationMetadataTests
{
    [Fact]
    public void WithETag_creates_metadata_with_only_etag()
    {
        var etag = EntityTagValue.Strong("abc123");

        var metadata = RepresentationMetadata.WithETag(etag);

        metadata.ETag.Should().Be(etag);
        metadata.LastModified.Should().BeNull();
        metadata.Vary.Should().BeNull();
        metadata.ContentLanguage.Should().BeNull();
        metadata.ContentLocation.Should().BeNull();
        metadata.AcceptRanges.Should().BeNull();
    }

    [Fact]
    public void WithStrongETag_creates_metadata_with_strong_etag()
    {
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        metadata.ETag.Should().NotBeNull();
        metadata.ETag!.OpaqueTag.Should().Be("abc123");
        metadata.ETag.IsWeak.Should().BeFalse();
        metadata.LastModified.Should().BeNull();
    }

    [Fact]
    public void Builder_SetLastModified_creates_metadata_with_only_last_modified()
    {
        var date = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var metadata = RepresentationMetadata.Create()
            .SetLastModified(date)
            .Build();

        metadata.LastModified.Should().Be(date);
        metadata.ETag.Should().BeNull();
        metadata.Vary.Should().BeNull();
        metadata.ContentLanguage.Should().BeNull();
        metadata.ContentLocation.Should().BeNull();
        metadata.AcceptRanges.Should().BeNull();
    }

    [Fact]
    public void Builder_all_properties_round_trip()
    {
        var etag = EntityTagValue.Weak("v42");
        var date = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var metadata = RepresentationMetadata.Create()
            .SetETag(etag)
            .SetLastModified(date)
            .AddVary("Accept", "Accept-Encoding")
            .AddContentLanguage("en", "fr")
            .SetContentLocation("/api/items/42")
            .SetAcceptRanges("bytes")
            .Build();

        metadata.ETag.Should().Be(etag);
        metadata.LastModified.Should().Be(date);
        metadata.Vary.Should().Equal(["Accept", "Accept-Encoding"]);
        metadata.ContentLanguage.Should().Equal(["en", "fr"]);
        metadata.ContentLocation.Should().Be("/api/items/42");
        metadata.AcceptRanges.Should().Be("bytes");
    }

    [Fact]
    public void Builder_AddVary_deduplicates_case_insensitively()
    {
        var metadata = RepresentationMetadata.Create()
            .AddVary("Accept", "accept", "ACCEPT", "Accept-Encoding")
            .Build();

        metadata.Vary.Should().Equal(["Accept", "Accept-Encoding"]);
    }

    [Fact]
    public void Builder_AddContentLanguage_deduplicates_case_insensitively()
    {
        var metadata = RepresentationMetadata.Create()
            .AddContentLanguage("en", "EN", "fr")
            .Build();

        metadata.ContentLanguage.Should().Equal(["en", "fr"]);
    }

    [Fact]
    public void Builder_SetStrongETag_sets_strong_etag()
    {
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("v1")
            .Build();

        metadata.ETag.Should().NotBeNull();
        metadata.ETag!.OpaqueTag.Should().Be("v1");
        metadata.ETag.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void Builder_SetWeakETag_sets_weak_etag()
    {
        var metadata = RepresentationMetadata.Create()
            .SetWeakETag("v1")
            .Build();

        metadata.ETag.Should().NotBeNull();
        metadata.ETag!.OpaqueTag.Should().Be("v1");
        metadata.ETag.IsWeak.Should().BeTrue();
    }

    [Fact]
    public void Builder_empty_build_returns_all_null()
    {
        var metadata = RepresentationMetadata.Create().Build();

        metadata.ETag.Should().BeNull();
        metadata.LastModified.Should().BeNull();
        metadata.Vary.Should().BeNull();
        metadata.ContentLanguage.Should().BeNull();
        metadata.ContentLocation.Should().BeNull();
        metadata.AcceptRanges.Should().BeNull();
    }

    [Fact]
    public void Builder_AddVary_across_multiple_calls_accumulates_and_deduplicates()
    {
        var metadata = RepresentationMetadata.Create()
            .AddVary("Accept")
            .AddVary("Accept-Encoding")
            .AddVary("Accept")
            .Build();

        metadata.Vary.Should().Equal(["Accept", "Accept-Encoding"]);
    }
}