namespace Trellis.Authorization.Tests;

public class ActorIdentityContractTests
{
    [Fact]
    public void IActorIdentityProvider_PublicContract_ResolvesOptionalIdentityWithoutChangingActorProvider()
    {
        var contract = typeof(IActorProvider).Assembly.GetType("Trellis.Authorization.IActorIdentityProvider");

        contract.Should().NotBeNull();
        contract!.IsInterface.Should().BeTrue();
        var method = contract.GetMethod("GetCurrentActorIdAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<ValueTask<Maybe<ActorId>>>();
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be<CancellationToken>();
        typeof(IActorProvider).GetMethods().Should().ContainSingle()
            .Which.Name.Should().Be(nameof(IActorProvider.GetCurrentActorAsync));
    }
}