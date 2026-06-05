namespace Trellis.Mediator.Tests;

using System.Reflection;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Tests that resource-authorization behavior registration is idempotent across modular composition paths.
/// </summary>
public class ResourceAuthorizationIdempotencyTests
{
    [Fact]
    public void AddResourceAuthorization_typed_called_twice_registers_exactly_one_behavior()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<Unit>>();
        services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<Unit>>();

        CountBehavior<UpdateOrderCommand, Order, Result<Unit>>(services)
            .Should().Be(1);
    }

    [Fact]
    public void AddResourceAuthorization_scan_called_twice_for_same_assembly_registers_exactly_one_per_command()
    {
        var services = new ServiceCollection();
        var assembly = typeof(UpdateOrderCommand).Assembly;

        services.AddResourceAuthorization(assembly);
        services.AddResourceAuthorization(assembly);

        var groups = services
            .Where(IsResourceAuthorizationBehaviorDescriptor)
            .Select(static descriptor => descriptor.ImplementationType!.GetGenericArguments())
            .GroupBy(static args => (Message: args[0], Resource: args[1], Response: args[2]))
            .ToList();

        groups.Should().NotBeEmpty();
        groups.Should().OnlyContain(static group => group.Count() == 1,
            "scanning the same assembly more than once must not duplicate a closed resource-authorization behavior");
    }

    [Fact]
    public void AddResourceAuthorization_explicit_then_scan_registers_exactly_one_for_overlapping_commands()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<Unit>>();
        services.AddResourceAuthorization(typeof(UpdateOrderCommand).Assembly);

        CountBehavior<UpdateOrderCommand, Order, Result<Unit>>(services)
            .Should().Be(1);
    }

    [Fact]
    public void AddResourceAuthorization_typed_for_different_responses_registers_both()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<DifferentResponseCommand, DifferentResponseResource, Result<Unit>>();
        services.AddResourceAuthorization<DifferentResponseCommand, DifferentResponseResource, Result<string>>();

        var descriptors = services
            .Where(IsResourceAuthorizationBehaviorDescriptor)
            .Where(static descriptor => descriptor.ImplementationType!.GetGenericArguments()[0] == typeof(DifferentResponseCommand))
            .ToList();

        descriptors.Should().HaveCount(2);
        descriptors.Select(static descriptor => descriptor.ImplementationType).Should().BeEquivalentTo(
            [
                typeof(ResourceAuthorizationBehavior<DifferentResponseCommand, DifferentResponseResource, Result<Unit>>),
                typeof(ResourceAuthorizationBehavior<DifferentResponseCommand, DifferentResponseResource, Result<string>>),
            ]);
    }

    [Fact]
    public void AddResourceAuthorization_typed_then_via_registers_both()
    {
        var services = new ServiceCollection();
        var pipelineType = typeof(IPipelineBehavior<DualModeIdempotencyCommand, Result<Unit>>);
        var typedBehaviorType = typeof(ResourceAuthorizationBehavior<DualModeIdempotencyCommand, DualModeResource, Result<Unit>>);
        var viaBehaviorType = typeof(ResourceAuthorizationViaBehavior<DualModeIdempotencyCommand, DualModeLeaf, DualModeOwner, Result<Unit>>);

        // Public registration APIs reject dual-mode commands before insertion; exercise the helper
        // directly to verify idempotency keys by implementation type, not only service type.
        InsertResourceAuthorizationBehavior(services, ServiceDescriptor.Scoped(pipelineType, typedBehaviorType));
        InsertResourceAuthorizationBehavior(services, ServiceDescriptor.Scoped(pipelineType, viaBehaviorType));

        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == pipelineType
                && (descriptor.ImplementationType == typedBehaviorType
                    || descriptor.ImplementationType == viaBehaviorType))
            .ToList();

        descriptors.Should().HaveCount(2);
        descriptors.Select(static descriptor => descriptor.ImplementationType).Should().BeEquivalentTo(
            [typedBehaviorType, viaBehaviorType]);
    }

    private static int CountBehavior<TMessage, TResource, TResponse>(IServiceCollection services)
        where TMessage : IAuthorizeResource<TResource>, IMessage
        where TResource : class
        where TResponse : IResult, IFailureFactory<TResponse>
        => services.Count(static descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<TMessage, TResponse>)
            && descriptor.ImplementationType == typeof(ResourceAuthorizationBehavior<TMessage, TResource, TResponse>));

    private static bool IsResourceAuthorizationBehaviorDescriptor(ServiceDescriptor descriptor)
        => descriptor.ImplementationType is { IsGenericType: true } implementationType
            && implementationType.GetGenericTypeDefinition() == typeof(ResourceAuthorizationBehavior<,,>);

    private static void InsertResourceAuthorizationBehavior(IServiceCollection services, ServiceDescriptor descriptor)
    {
        var method = typeof(ServiceCollectionExtensions).GetMethod(
            "InsertResourceAuthorizationBehavior",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [services, descriptor]);
    }
}

internal sealed record UpdateOrderCommand(string OrderId)
    : ICommand<Result<Unit>>, IAuthorizeResource<Order>
{
    public IResult Authorize(Actor actor, Order resource) => Result.Ok();
}

internal sealed record Order(string Id);

internal sealed record DifferentResponseCommand(string ResourceId)
    : IMessage, IAuthorizeResource<DifferentResponseResource>
{
    public IResult Authorize(Actor actor, DifferentResponseResource resource) => Result.Ok();
}

internal sealed record DifferentResponseResource(string Id);

internal sealed record DualModeIdempotencyCommand(string Id)
    : IMessage,
      IAuthorizeResource<DualModeResource>,
      IAuthorizeResourceVia<DualModeOwner>
{
    public IResult Authorize(Actor actor, DualModeResource resource) => Result.Ok();

    public IResult Authorize(Actor actor, IReadOnlyList<DualModeOwner> owners) => Result.Ok();
}

internal sealed record DualModeResource(string Id);
internal sealed record DualModeLeaf(string Id);
internal sealed record DualModeOwner(string Id);