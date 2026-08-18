namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class InboxRegistrationTests
{
    [Fact]
    public void AddTrellisInbox_called_twice_applies_the_second_configure()
    {
        var services = new ServiceCollection();

        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "first");
        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "second");

        services.BuildServiceProvider().GetRequiredService<InboxOptions>()
            .ConsumerId.Should().Be("second");
    }

    [Fact]
    public void AddTrellisInbox_called_twice_registers_a_single_options_instance()
    {
        var services = new ServiceCollection();

        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "first");
        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "second");

        services.Count(d => d.ServiceType == typeof(InboxOptions)).Should().Be(1);
    }

    [Fact]
    public void AddTrellisInbox_validates_the_accumulated_options()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = string.Empty);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddTrellisInbox_failed_second_configure_leaves_earlier_options_intact()
    {
        var services = new ServiceCollection();
        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "first");

        var act = () => services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = string.Empty);

        act.Should().Throw<InvalidOperationException>();

        services.BuildServiceProvider().GetRequiredService<InboxOptions>()
            .ConsumerId.Should().Be("first", "a rejected registration must not be partially applied");
    }

    [Fact]
    public void InboxOptions_Clone_copies_every_public_settable_property()
    {
        // Guards the hand-written Clone() against a property being added and not copied,
        // which would silently drop that setting on a repeated registration.
        var source = new InboxOptions { ConsumerId = "consumer-42" };

        var clone = source.Clone();

        var defaults = new InboxOptions();
        foreach (var property in typeof(InboxOptions).GetProperties().Where(p => p.CanWrite && p.CanRead))
        {
            property.GetValue(source).Should().NotBe(property.GetValue(defaults),
                $"the test must set {property.Name} to a non-default value for the assertion below to be meaningful");
            property.GetValue(clone).Should().Be(property.GetValue(source),
                $"InboxOptions.Clone() must copy {property.Name}");
        }
    }

    [Fact]
    public void AddTrellisInbox_throws_when_a_factory_owns_the_options_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new InboxOptions { ConsumerId = "theirs" });

        var act = () => services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "ours");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered by a factory or implementation type*");
    }

    [Fact]
    public void AddTrellisInbox_configures_the_options_instance_the_container_actually_resolves()
    {
        // A later descriptor wins in MS DI, so the helper must layer onto the last one rather than
        // configuring an earlier instance the dispatcher would never receive.
        var services = new ServiceCollection();
        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "first");
        services.AddSingleton(new InboxOptions { ConsumerId = "theirs" });

        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "second");

        services.BuildServiceProvider().GetRequiredService<InboxOptions>()
            .ConsumerId.Should().Be("second");
        services.Count(d => d.ServiceType == typeof(InboxOptions)).Should().Be(2);
    }

    [Fact]
    public void AddTrellisInbox_ignores_keyed_options_registrations()
    {
        // Keyed descriptors take no part in unkeyed resolution, and reading ImplementationInstance
        // on one throws — so they must not be mistaken for the dispatcher's options registration.
        var services = new ServiceCollection();
        services.AddKeyedSingleton("other", new InboxOptions { ConsumerId = "keyed" });

        services.AddTrellisInbox<InboxRegistrationDbContext>(o => o.ConsumerId = "unkeyed");

        services.BuildServiceProvider().GetRequiredService<InboxOptions>()
            .ConsumerId.Should().Be("unkeyed");
    }

    private sealed class InboxRegistrationDbContext(DbContextOptions<InboxRegistrationDbContext> options)
        : DbContext(options);
}