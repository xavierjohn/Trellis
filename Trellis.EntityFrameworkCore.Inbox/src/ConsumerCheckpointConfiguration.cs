namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core mapping for <see cref="ConsumerCheckpoint"/>. Applied to a consumer's model via
/// <see cref="CheckpointModelBuilderExtensions.AddTrellisConsumerCheckpoints(ModelBuilder)"/>.
/// </summary>
public sealed class ConsumerCheckpointConfiguration : IEntityTypeConfiguration<ConsumerCheckpoint>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConsumerCheckpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TrellisConsumerCheckpoints");

        // One row per consumer — the resume cursor is single-valued per subscriber.
        builder.HasKey(c => c.ConsumerId);

        // Same width as the inbox dedup key so a consumer that uses both keys them consistently.
        builder.Property(c => c.ConsumerId).HasMaxLength(InboxOptions.MaxConsumerIdLength).IsRequired();
        builder.Property(c => c.Position).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
    }
}
