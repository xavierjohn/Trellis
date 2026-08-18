namespace Trellis.Asp.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Xunit;

/// <summary>
/// Guards the contract that <c>AddResourceCollectionName&lt;TResource&gt;</c> keys overrides on the
/// same resource-type name <see cref="ResourceRef.For{TResource}(object?)"/> emits. Registration and
/// lookup are separate code paths, so a divergence produces no error — the override simply never
/// matches and callers silently fall back to the naive plural.
/// </summary>
public sealed class ResourceCollectionNameRegistrationTests
{
    private sealed class Person;

    private static ResourceCollectionNameRegistry BuildRegistry(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<ResourceCollectionNameRegistry>();

    [Fact]
    public void AddResourceCollectionName_generic_keys_on_the_name_For_emits()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Person>("people");

        BuildRegistry(services).Resolve(ResourceRef.For<Person>().Type).Should().Be("people");
    }

    [Fact]
    public void AddResourceCollectionName_generic_keys_on_the_name_For_emits_for_Maybe_wrapped_resources()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Maybe<Person>>("people");

        BuildRegistry(services).Resolve(ResourceRef.For<Maybe<Person>>().Type).Should().Be("people");
    }

    [Fact]
    public void AddResourceCollectionName_generic_unwraps_Maybe_to_the_same_key_as_the_bare_resource()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Maybe<Person>>("people");

        BuildRegistry(services).Resolve(ResourceRef.For<Person>().Type).Should().Be("people");
    }

    [Fact]
    public void AddResourceCollectionName_generic_treats_nested_Maybe_as_the_innermost_resource()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Maybe<Maybe<Person>>>("people");

        BuildRegistry(services).Resolve(ResourceRef.For<Person>().Type).Should().Be("people");
    }

    [Fact]
    public void Registering_the_bare_and_Maybe_wrapped_resource_with_the_same_name_does_not_conflict()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Person>("people");
        services.AddResourceCollectionName<Maybe<Person>>("people");

        BuildRegistry(services).Resolve(ResourceRef.For<Person>().Type).Should().Be("people");
    }

    [Fact]
    public void Unregistered_resource_still_falls_back_to_the_naive_plural()
    {
        var services = new ServiceCollection();
        services.AddResourceCollectionName<Person>("people");

        BuildRegistry(services).Resolve("Order").Should().Be("orders");
    }
}
