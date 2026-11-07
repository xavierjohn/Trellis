namespace Trellis.Http.Abstractions.Tests;

using Trellis.Testing;

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
            .Build();

        metadata.ETag.Should().Be(etag);
        metadata.LastModified.Should().Be(date);
        metadata.Vary.Should().Equal(["Accept", "Accept-Encoding"]);
        metadata.ContentLanguage.Should().Equal(["en", "fr"]);
        metadata.ContentLocation.Should().Be("/api/items/42");
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

    [Fact]
    public void WithETag_wildcard_is_rejected()
    {
        var act = () => RepresentationMetadata.WithETag(EntityTagValue.Wildcard());

        act.ShouldRejectArgument("eTag");
    }

    [Fact]
    public void Builder_SetETag_wildcard_is_rejected_when_built()
    {
        var act = () => RepresentationMetadata.Create()
            .SetETag(EntityTagValue.Wildcard())
            .Build();

        act.ShouldRejectArgument("eTag");
    }

    [Fact]
    public void A_literal_star_tag_is_not_the_wildcard_and_is_accepted()
    {
        var viaFactory = RepresentationMetadata.WithETag(EntityTagValue.Strong("*"));
        var viaBuilder = RepresentationMetadata.Create().SetWeakETag("*").Build();

        viaFactory.ETag!.OpaqueTag.Should().Be("*");
        viaFactory.ETag.IsWildcard.Should().BeFalse();
        viaBuilder.ETag!.OpaqueTag.Should().Be("*");
        viaBuilder.ETag.IsWeak.Should().BeTrue();
    }

    private static string[] Names(params string[] values) => values;

    public static TheoryData<string[]> BlankOnlyLists() =>
        new()
        {
            Names(),
            Names(string.Empty),
            Names(" ", "\t"),
        };

    [Theory]
    [MemberData(nameof(BlankOnlyLists))]
    public void Builder_AddVary_with_only_blank_names_builds_null(string[] fieldNames)
    {
        var metadata = RepresentationMetadata.Create().AddVary(fieldNames).Build();

        metadata.Vary.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BlankOnlyLists))]
    public void Builder_AddContentLanguage_with_only_blank_values_builds_null(string[] languages)
    {
        var metadata = RepresentationMetadata.Create().AddContentLanguage(languages).Build();

        metadata.ContentLanguage.Should().BeNull();
    }

    [Fact]
    public void Builder_blank_entries_are_skipped_and_valid_ones_kept()
    {
        var metadata = RepresentationMetadata.Create()
            .AddVary(" ", "Accept", string.Empty)
            .AddContentLanguage("\t", "en", string.Empty)
            .Build();

        metadata.Vary.Should().Equal(["Accept"]);
        metadata.ContentLanguage.Should().Equal(["en"]);
    }

    [Fact]
    public void Build_returns_a_snapshot_that_later_builder_changes_do_not_alter()
    {
        var firstDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var builder = RepresentationMetadata.Create()
            .SetStrongETag("first")
            .SetLastModified(firstDate)
            .AddVary("Accept")
            .AddContentLanguage("en")
            .SetContentLocation("/first");
        var first = builder.Build();

        var second = builder
            .SetWeakETag("second")
            .SetLastModified(firstDate.AddDays(1))
            .AddVary("Accept-Encoding")
            .AddContentLanguage("fr")
            .SetContentLocation("/second")
            .Build();

        first.ETag.Should().Be(EntityTagValue.Strong("first"));
        first.LastModified.Should().Be(firstDate);
        first.Vary.Should().Equal(["Accept"]);
        first.ContentLanguage.Should().Equal(["en"]);
        first.ContentLocation.Should().Be("/first");
        second.ETag.Should().Be(EntityTagValue.Weak("second"));
        second.LastModified.Should().Be(firstDate.AddDays(1));
        second.Vary.Should().Equal(["Accept", "Accept-Encoding"]);
        second.ContentLanguage.Should().Equal(["en", "fr"]);
        second.ContentLocation.Should().Be("/second");
    }
}
