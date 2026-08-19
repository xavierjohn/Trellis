namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core mapping for <see cref="OutboxMessage"/>. Applied to a consumer's model via
/// <see cref="OutboxModelBuilderExtensions.AddTrellisOutbox(ModelBuilder)"/>.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    // Handler type names cannot contain a newline, so a newline-delimited list needs no escaping and stays
    // greppable in the database — deliberately chosen over JSON, which would add quoting noise to a column
    // that exists for operator triage as much as for the relay.
    private const char CompletedHandlerSeparator = '\n';

    private static readonly ValueComparer<IReadOnlyList<string>> s_completedHandlersComparer = new(
        (left, right) => left!.SequenceEqual(right!, StringComparer.Ordinal),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
        value => value.ToArray());

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

        // Per-handler retry bookkeeping. Non-nullable with an empty default so adding the column to an
        // existing outbox table backfills cleanly: pre-existing rows simply have no completed handlers.
        builder.Property(m => m.CompletedHandlers)
            .HasConversion(
                value => string.Join(CompletedHandlerSeparator, value),
                value => value.Length == 0
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : value.Split(CompletedHandlerSeparator),
                s_completedHandlersComparer)
            .HasDefaultValue(Array.Empty<string>())
            .IsRequired();

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