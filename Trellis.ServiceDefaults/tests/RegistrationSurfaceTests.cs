namespace Trellis.ServiceDefaults.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Encodes the <c>AddXxx</c> / <c>UseXxx</c> rule that governs which service-registration helpers
/// must be surfaced as a <see cref="TrellisServiceBuilder"/> slot.
/// </summary>
/// <remarks>
/// <para>
/// The rule: a <em>composition-root feature</em> — anything an application author turns on, such as
/// a pipeline behavior, dispatcher, actor provider, or the outbox/inbox — must have a matching
/// <c>TrellisServiceBuilder.UseXxx(...)</c> slot so it participates in canonical ordering.
/// <em>Leaf, store, and adapter-author</em> extension points must not, because they are invoked by
/// another helper that is itself surfaced, or by an adapter author wiring a provider Trellis does
/// not ship.
/// </para>
/// <para>
/// The rule previously lived only in prose in <c>.github/copilot-instructions.md</c>, alongside a
/// hand-maintained exception list that drifted. These tests make it mechanical: every registration
/// helper reachable from <c>Trellis.ServiceDefaults</c> must be classified below, and a new helper
/// fails the build until someone records the decision and the reason for it. A classification that
/// names a slot must also name one that exists <em>and</em> that
/// <c>Trellis.ServiceDefaults</c> actually references — a slot name alone would prove only that
/// somebody typed a plausible string.
/// </para>
/// <para>
/// Vendor-SDK provider packages (<c>Trellis.Asp.Idempotency.Cosmos</c>,
/// <c>Trellis.Messaging.AzureServiceBus</c>) are out of scope here, and deliberately so: they are
/// absent from this assembly's reference graph precisely because <c>Trellis.ServiceDefaults</c>
/// references no vendor SDK. Their "no slot" status is therefore enforced by the dependency graph
/// itself rather than by an allowlist entry.
/// </para>
/// </remarks>
public class RegistrationSurfaceTests
{
    /// <summary>
    /// Classification of a registration helper: either the <c>UseXxx</c> slot that must surface it,
    /// or <see langword="null"/> for a leaf/adapter registration that must not have one.
    /// </summary>
    /// <param name="Slot">Name of the required <see cref="TrellisServiceBuilder"/> method, or <see langword="null"/>.</param>
    /// <param name="Reason">Why the helper is or is not a composition-root feature.</param>
    private sealed record Classification(string? Slot, string Reason);

    private static readonly Dictionary<string, Classification> Expected = new(StringComparer.Ordinal)
    {
        // ---- Composition-root features: an application author turns these on. ----
        ["Trellis.Asp::AddTrellisAsp"] = new("UseAsp", "Result-to-response mapping."),
        ["Trellis.Asp::AddScalarValueValidation"] = new("UseScalarValueValidation", "MVC binder and JSON converter wiring."),
        ["Trellis.Asp::AddTrellisProblemDetails"] = new("UseProblemDetails", "RFC 9457 customization."),
        ["Trellis.Asp::AddTrellisIdempotency"] = new("UseIdempotency", "Idempotency middleware and options."),
        ["Trellis.Asp::AddClaimsActorProvider"] = new("UseClaimsActorProvider", "Actor provider."),
        ["Trellis.Asp::AddNestedJsonPathClaimsActorProvider"] = new("UseNestedJsonPathClaimsActorProvider", "Actor provider."),
        ["Trellis.Asp::AddEntraActorProvider"] = new("UseEntraActorProvider", "Actor provider."),
        ["Trellis.Asp::AddEasyAuthActorProvider"] = new("UseEasyAuthActorProvider", "Actor provider."),
        ["Trellis.Asp::AddDevelopmentActorProvider"] = new("UseDevelopmentActorProvider", "Actor provider."),
        ["Trellis.Asp::AddCachingActorProvider"] = new("UseCachingActorProvider", "Decorates the ambient actor provider."),
        ["Trellis.Asp::AddTrellisWorkerActor"] = new("UseWorkerActor", "Ambient actor for non-HTTP hosts."),
        ["Trellis.Mediator::AddTrellisBehaviors"] = new("UseMediator", "Core pipeline behaviors."),
        ["Trellis.Mediator::AddResourceAuthorization"] = new("UseResourceAuthorization", "Resource-authorization behavior."),
        ["Trellis.Mediator::AddRelatedResourceAuthorization"] = new("UseRelatedResourceAuthorization", "Related-resource authorization behavior."),
        ["Trellis.Mediator::AddDomainEventDispatch"] = new("UseDomainEvents", "Domain-event dispatcher."),
        ["Trellis.Mediator::AddIntegrationEventDispatch"] = new("UseIntegrationEvents", "Integration-event dispatcher."),
        ["Trellis.Mediator::AddTrackedAggregateDomainEventDispatch"] = new("UseTrackedAggregateDomainEvents", "Tracked-aggregate domain-event dispatcher."),
        ["Trellis.Mediator.FluentValidation::AddTrellisFluentValidation"] = new("UseFluentValidation", "Validation behavior."),
        ["Trellis.EntityFrameworkCore::AddTrellisUnitOfWork"] = new("UseEntityFrameworkUnitOfWork", "Unit of work plus transactional behavior."),
        ["Trellis.EntityFrameworkCore.Outbox::AddTrellisOutbox"] = new("UseOutbox", "Transactional outbox relay."),
        ["Trellis.EntityFrameworkCore.Inbox::AddTrellisInbox"] = new("UseInbox", "Idempotent consumption."),

        // ---- Leaf / store / adapter-author registrations: no slot by design. ----
        ["Trellis.ServiceDefaults::AddTrellis"] = new(null, "The entry point that creates the builder; it cannot be a slot on itself."),
        ["Trellis.Asp::AddInMemoryIdempotencyStore"] = new(null, "Default leaf store. UseIdempotency() registers the middleware and options but deliberately no store, so the application picks one explicitly alongside it; adapter authors substitute their own."),
        ["Trellis.Asp::AddTrellisRouteConstraint"] = new(null, "Registers one route constraint; content, not a feature toggle."),
        ["Trellis.Asp::AddTrellisRouteConstraints"] = new(null, "Assembly scan over the same per-constraint content."),
        ["Trellis.Asp::AddResourceCollectionName"] = new(null, "Per-resource metadata; content, not a feature toggle."),
        ["Trellis.Asp::AddResourceCollectionNames"] = new(null, "Assembly scan over the same per-resource metadata."),
        ["Trellis.Mediator::AddTransactionalCommandBehavior"] = new(null, "Provider-neutral; invoked by AddTrellisUnitOfWork, which UseEntityFrameworkUnitOfWork surfaces."),
        ["Trellis.Mediator::AddDomainEventHandler"] = new(null, "Registers one handler; invoked by the typed UseDomainEvents overload."),
        ["Trellis.Mediator::AddIntegrationEventHandler"] = new(null, "Registers one handler; invoked by the typed UseIntegrationEvents overload."),
        ["Trellis.Mediator::AddResourceLoaders"] = new(null, "Assembly scan over per-resource loaders; content for UseResourceAuthorization."),
        ["Trellis.Mediator::AddSharedResourceLoader"] = new(null, "Registers one loader; content for UseResourceAuthorization."),
        ["Trellis.EntityFrameworkCore::AddTrellisUnitOfWorkWithoutBehavior"] = new(null, "Escape hatch for hosts that own pipeline ordering; slotting it would contradict its purpose."),
        ["Trellis.EntityFrameworkCore.Inbox::AddTrellisConsumerCheckpointStore"] = new(null, "Store for consumers that track checkpoints without the full inbox."),

        // ---- Deliberate exceptions to the shape of the rule. ----
        ["Trellis.Asp::AddTrellisAspWithScalarValidation"] = new(null, "Convenience composite of AddTrellisAsp + AddScalarValueValidation; both halves are already slotted, and a slot here would offer a second way to express one ordering."),
        ["Trellis.Asp::AddScalarValueValidationForMinimalApi"] = new(null, "Minimal API wiring is deliberately outside the UseScalarValueValidation slot, which documents that hosts add the middleware and per-endpoint filter themselves."),
    };

    private static readonly Assembly[] ComposedAssemblies =
    [
        typeof(TrellisServiceBuilder).Assembly,
        typeof(Trellis.Asp.TrellisAspOptions).Assembly,
        typeof(Trellis.Mediator.ResourceAuthorizationOptions).Assembly,
        typeof(Trellis.Mediator.FluentValidation.FluentValidationServiceCollectionExtensions).Assembly,
        typeof(Trellis.EntityFrameworkCore.UnitOfWorkServiceCollectionExtensions).Assembly,
        typeof(Trellis.EntityFrameworkCore.OutboxServiceCollectionExtensions).Assembly,
        typeof(Trellis.EntityFrameworkCore.InboxServiceCollectionExtensions).Assembly,
    ];

    [Fact]
    public void Every_registration_helper_is_classified_as_slotted_or_deliberately_unslotted()
    {
        var unclassified = DiscoverRegistrationHelpers().Except(Expected.Keys, StringComparer.Ordinal).ToList();

        unclassified.Should().BeEmpty(
            "every IServiceCollection registration helper must record whether it is a composition-root "
            + "feature (which needs a TrellisServiceBuilder.UseXxx slot) or a leaf/adapter registration "
            + "(which must not have one). Add {0} to RegistrationSurfaceTests.Expected with a reason",
            string.Join(", ", unclassified));
    }

    [Fact]
    public void Classification_table_has_no_stale_entries()
    {
        var discovered = DiscoverRegistrationHelpers().ToHashSet(StringComparer.Ordinal);
        var stale = Expected.Keys.Where(k => !discovered.Contains(k)).ToList();

        stale.Should().BeEmpty(
            "a classified helper no longer exists, so the table describes a surface that is gone. "
            + "Remove {0} from RegistrationSurfaceTests.Expected",
            string.Join(", ", stale));
    }

    [Fact]
    public void Every_composition_root_feature_has_its_builder_slot()
    {
        var slotNames = typeof(TrellisServiceBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = Expected
            .Where(e => e.Value.Slot is not null && !slotNames.Contains(e.Value.Slot))
            .Select(e => $"{e.Key} -> {e.Value.Slot}")
            .ToList();

        missing.Should().BeEmpty(
            "each composition-root registration must be reachable through the builder so canonical "
            + "pipeline ordering is preserved. Missing slots: {0}",
            string.Join(", ", missing));
    }

    [Fact]
    public void Every_composition_root_feature_is_actually_called_by_the_builder()
    {
        var called = ReferencedMemberNames(typeof(TrellisServiceBuilder).Assembly);

        var unwired = Expected
            .Where(e => e.Value.Slot is not null)
            .Select(e => e.Key)
            .Where(key => !called.Contains(key[(key.IndexOf("::", StringComparison.Ordinal) + 2)..]))
            .ToList();

        unwired.Should().BeEmpty(
            "a slot name that merely exists proves nothing — Trellis.ServiceDefaults must actually "
            + "reference the classified helper, or the feature is unreachable through the builder "
            + "despite being classified as slotted. Unwired: {0}",
            string.Join(", ", unwired));
    }

    [Fact]
    public void Registration_helper_names_are_unique_within_each_assembly()
    {
        var collisions = ComposedAssemblies
            .Distinct()
            .SelectMany(RegistrationHelpers)
            .GroupBy(m => $"{m.DeclaringType!.Assembly.GetName().Name}::{m.Name}", StringComparer.Ordinal)
            .Where(g => g.Select(m => m.DeclaringType!.FullName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToList();

        collisions.Should().BeEmpty(
            "the classification table is keyed on assembly plus method name, so two same-named helpers "
            + "on different static classes in one assembly would collapse into a single entry and let "
            + "one of them escape the gate. Colliding keys: {0}",
            string.Join(", ", collisions));
    }

    /// <summary>
    /// Reads the names in the assembly's <c>MemberRef</c> table, which is every member it references
    /// across an assembly boundary. Cheaper and far more robust than decoding IL, and sufficient here:
    /// the question is whether <c>Trellis.ServiceDefaults</c> calls the helper at all.
    /// </summary>
    private static HashSet<string> ReferencedMemberNames(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        return reader.MemberReferences
            .Select(handle => reader.GetString(reader.GetMemberReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> DiscoverRegistrationHelpers() =>
        ComposedAssemblies
            .Distinct()
            .SelectMany(RegistrationHelpers)
            .Select(m => $"{m.DeclaringType!.Assembly.GetName().Name}::{m.Name}")
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<MethodInfo> RegistrationHelpers(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(t => t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false))
            .Where(m => m.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(IServiceCollection));
}
