namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core mapping for <see cref="InboxMessage"/>. Applied to a consumer's model via
/// <see cref="InboxModelBuilderExtensions.AddTrellisInbox(ModelBuilder)"/>.
/// </summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TrellisInboxMessages");

        // Dedup key: one row per (consumer, message). The composite PK is also the uniqueness guard that
        // makes a concurrent duplicate fail at SaveChanges so the dispatcher can treat it as already done.
        builder.HasKey(m => new { m.ConsumerId, m.MessageId });

        builder.Property(m => m.ConsumerId).HasMaxLength(InboxOptions.MaxConsumerIdLength).IsRequired();
        builder.Property(m => m.EventType).IsRequired();
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.ProcessedAt).IsRequired();

        // Supports pruning rows older than the transport's redelivery window.
        builder.HasIndex(m => m.ProcessedAt);
    }
}