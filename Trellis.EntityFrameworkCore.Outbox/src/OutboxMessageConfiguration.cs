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

        builder.Property(m => m.LockedUntil);
        // LockedBy is an optimistic concurrency token: the relay's bookkeeping UPDATE only lands while this
        // drain still owns the row, so a drain that outlived its lease cannot clobber the instance that
        // reclaimed the row.
        builder.Property(m => m.LockedBy).IsConcurrencyToken();

        // Covering index for the relay's "pending, claimable, in order" scan.
        builder.HasIndex(m => new { m.ProcessedAt, m.LockedUntil, m.Sequence });

        // Index for loading a drain's just-claimed batch by its claim token.
        builder.HasIndex(m => m.LockedBy);
    }
}
