namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core mapping for <see cref="OutboxMessage"/>. Applied to a consumer's model via
/// <see cref="OutboxModelBuilderExtensions.AddTrellisOutbox(ModelBuilder)"/>.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TrellisOutboxMessages");

        builder.HasKey(m => m.Sequence);
        builder.Property(m => m.Sequence).ValueGeneratedOnAdd();

        builder.Property(m => m.Id).IsRequired();
        builder.HasIndex(m => m.Id).IsUnique();

        builder.Property(m => m.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.EventType).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.Attempts).IsRequired();

        // Covering index for the relay's "pending, in order" scan.
        builder.HasIndex(m => new { m.ProcessedAt, m.Sequence });
    }
}
