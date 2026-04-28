namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Asserts the canonical relative ordering of pipeline behaviors registered by
/// <see cref="ServiceCollectionExtensions"/> (and the closed-generic insertion done by
/// <see cref="ServiceCollectionExtensions.AddResourceAuthorization{TMessage, TResource, TResponse}"/>).
/// <para>
/// The canonical Trellis pipeline (outermost to innermost) is documented on
/// <see cref="ServiceCollectionExtensions.PipelineBehaviors"/>:
/// Exception → Tracing → Logging → Authorization → ResourceAuthorization → Validation →
/// TransactionalCommand (opt-in, <c>Trellis.EntityFrameworkCore</c>).
/// </para>
/// <para>
/// FluentValidation (opt-in, <c>Trellis.FluentValidation</c>) does <b>not</b> occupy a
/// pipeline slot; it contributes <c>FluentValidationMessageValidatorAdapter&lt;TMessage&gt;</c>
/// to the existing <see cref="ValidationBehavior{TMessage, TResponse}"/> via the
/// <see cref="IMessageValidator{TMessage}"/> abstraction. It is covered by
/// <c>FluentValidationMessageValidatorAdapterTests</c>; the transactional behavior is covered
/// by <c>UnitOfWorkServiceCollectionExtensionsTests</c>.
/// </para>
/// </summary>
public class PipelineOrderingTests
{
    [Fact]
    public void PipelineBehaviors_exposes_five_always_on_behaviors_in_canonical_order()
        => ServiceCollectionExtensions.PipelineBehaviors.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>));

    [Fact]
    public void AddTrellisBehaviors_registers_five_open_generics_in_canonical_order()
    {
        var services = new ServiceCollection();

        services.AddTrellisBehaviors();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        behaviors.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void AddResourceAuthorization_inserts_closed_generic_immediately_before_validation()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
            .ToList();

        // Order should be: Exception, Tracing, Logging, Authorization, ResourceAuthorization (closed), Validation
        behaviors.Should().HaveCount(6);
        behaviors[0].ImplementationType.Should().Be(typeof(ExceptionBehavior<,>));
        behaviors[1].ImplementationType.Should().Be(typeof(TracingBehavior<,>));
        behaviors[2].ImplementationType.Should().Be(typeof(LoggingBehavior<,>));
        behaviors[3].ImplementationType.Should().Be(typeof(AuthorizationBehavior<,>));
        behaviors[4].ImplementationType.Should().Be<
            ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>>();
        behaviors[5].ImplementationType.Should().Be(typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void AddResourceAuthorization_with_no_validation_registered_appends_at_end()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        var descriptors = services.ToList();
        descriptors.Should().ContainSingle(d =>
            d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void AddTrellisBehaviors_is_idempotent_when_called_twice()
    {
        // ga-10: AddTrellisBehaviors must be safe to call from a plug-in extension method
        // (e.g. AddTrellisFluentValidation, AddTrellisAsp) that calls it as a precondition,
        // even if the consumer has already wired the behaviors up themselves.
        var services = new ServiceCollection();

        services.AddTrellisBehaviors();
        services.AddTrellisBehaviors();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviors.Should().HaveCount(5);
        behaviors.Select(d => d.ImplementationType).Should().BeEquivalentTo(
            ServiceCollectionExtensions.PipelineBehaviors,
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void AddTrellisBehaviors_registers_safe_by_default_TelemetryOptions()
    {
        // ga-12: Telemetry redaction options are registered as a singleton with
        // IncludeErrorDetail = false so behaviors don't leak Error.Detail into logs/traces.
        var services = new ServiceCollection();

        services.AddTrellisBehaviors();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<TrellisMediatorTelemetryOptions>();

        options.Should().NotBeNull();
        options!.IncludeErrorDetail.Should().BeFalse(
            "Detail can carry user input or PII and is opt-in for telemetry surfaces");
    }

    [Fact]
    public void AddTrellisBehaviors_with_configure_applies_TelemetryOptions()
    {
        var services = new ServiceCollection();

        services.AddTrellisBehaviors(o => o.IncludeErrorDetail = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TrellisMediatorTelemetryOptions>();
        options.IncludeErrorDetail.Should().BeTrue();
    }

    [Fact]
    public void AddTrellisBehaviors_with_configure_overrides_prior_default_registration()
    {
        var services = new ServiceCollection();

        services.AddTrellisBehaviors();
        services.AddTrellisBehaviors(o => o.IncludeErrorDetail = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TrellisMediatorTelemetryOptions>();
        options.IncludeErrorDetail.Should().BeTrue(
            "an explicit configure call must win over an earlier default-only registration");
    }
}