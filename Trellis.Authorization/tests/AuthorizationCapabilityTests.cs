namespace Trellis.Authorization.Tests;

using System.Reflection;

public class AuthorizationCapabilityTests
{
    [Theory]
    [InlineData(typeof(IAuthorize), "IAuthorizationMessage")]
    [InlineData(typeof(IAuthorizeResource<object>), "IAuthorizationMessage")]
    [InlineData(typeof(IAuthorizeResource<object>), "IResourceAuthorizationMessage")]
    [InlineData(typeof(IAuthorizeResourceVia<int>), "IAuthorizationMessage")]
    [InlineData(typeof(IAuthorizeResourceVia<int>), "IResourceAuthorizationMessage")]
    public void AuthorizationContract_ExistingInterface_InheritsCapability(Type contract, string capability)
        => contract.GetInterfaces().Select(type => type.Name).Should().Contain(capability);

    [Fact]
    public void IAuthorizeResource_ResourceParameter_RemainsContravariant()
        => typeof(IAuthorizeResource<>).GetGenericArguments()[0].GenericParameterAttributes
            .Should().Be(GenericParameterAttributes.Contravariant);

    [Fact]
    public void AuthorizationContract_ExistingMethods_PreservesSignatures()
    {
        typeof(IAuthorize).GetProperty(nameof(IAuthorize.RequiredPermissions))!
            .PropertyType.Should().Be<IReadOnlyList<string>>();
        typeof(IAuthorizeResource<object>).GetMethod(nameof(IAuthorizeResource<object>.Authorize))!
            .ReturnType.Should().Be<IResult>();
        typeof(IAuthorizeResourceVia<int>).GetMethod(nameof(IAuthorizeResourceVia<int>.Authorize))!
            .GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(Actor), typeof(IReadOnlyList<int>));
    }
}
