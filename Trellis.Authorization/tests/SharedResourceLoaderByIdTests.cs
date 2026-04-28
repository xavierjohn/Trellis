using Trellis.Testing;
namespace Trellis.Authorization.Tests;

/// <summary>
/// Tests for <see cref="SharedResourceLoaderById{TResource, TId}"/>.
/// </summary>
public class SharedResourceLoaderByIdTests
{
    #region GetByIdAsync delegates to implementation

    [Fact]
    public async Task GetByIdAsync_ReturnsResource_WhenFound()
    {
        var order = new TestOrder("order-1", "owner-1");
        var loader = new TestOrderSharedLoader(order);

        var result = await loader.GetByIdAsync("order-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeSameAs(order);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsFailure_WhenNotFound()
    {
        var loader = new TestOrderSharedLoader(resource: null);

        var result = await loader.GetByIdAsync("nonexistent", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    #endregion

    #region CancellationToken is propagated

    [Fact]
    public async Task GetByIdAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var loader = new TrackingSharedLoader();

        await loader.GetByIdAsync("test-id", cts.Token);

        loader.LastCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region ID is passed correctly

    [Fact]
    public async Task GetByIdAsync_PassesCorrectId()
    {
        var loader = new TrackingSharedLoader();

        await loader.GetByIdAsync("tracked-id", CancellationToken.None);

        loader.LastRequestedId.Should().Be("tracked-id");
    }

    #endregion

    #region Test helpers

    private sealed record TestOrder(string Id, string OwnerId);

    private sealed class TestOrderSharedLoader : SharedResourceLoaderById<TestOrder, string>
    {
        private readonly TestOrder? _resource;

        public TestOrderSharedLoader(TestOrder? resource) => _resource = resource;

        public override Task<Result<TestOrder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            _resource is not null
                ? Task.FromResult(Result.Ok(_resource))
                : Task.FromResult(Result.Fail<TestOrder>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"Order '{id}' not found." }));
    }

    private sealed class TrackingSharedLoader : SharedResourceLoaderById<TestOrder, string>
    {
        public string? LastRequestedId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public override Task<Result<TestOrder>> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Result.Fail<TestOrder>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        }
    }

    #endregion
}