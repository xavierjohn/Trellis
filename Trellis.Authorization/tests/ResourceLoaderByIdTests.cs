using Trellis.Testing;
namespace Trellis.Authorization.Tests;

/// <summary>
/// Tests for <see cref="ResourceLoaderById{TMessage, TResource, TId}"/>.
/// </summary>
public class ResourceLoaderByIdTests
{
    #region LoadAsync extracts ID and delegates to GetByIdAsync

    [Fact]
    public async Task LoadAsync_ExtractsIdAndReturnsResource()
    {
        var order = new TestOrder("order-1", "owner-1");
        var loader = new TestOrderLoader(order);
        var message = new LoadOrderMessage("order-1");

        var result = await loader.LoadAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeSameAs(order);
    }

    [Fact]
    public async Task LoadAsync_ResourceNotFound_ReturnsFailure()
    {
        var loader = new TestOrderLoader(resource: null);
        var message = new LoadOrderMessage("nonexistent");

        var result = await loader.LoadAsync(message, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<NotFoundError>();
    }

    #endregion

    #region GetId is called with the message

    [Fact]
    public async Task LoadAsync_PassesCorrectIdToGetByIdAsync()
    {
        var loader = new TrackingOrderLoader();
        var message = new LoadOrderMessage("tracked-id");

        await loader.LoadAsync(message, CancellationToken.None);

        loader.LastRequestedId.Should().Be("tracked-id");
    }

    #endregion

    #region CancellationToken is propagated

    [Fact]
    public async Task LoadAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var loader = new TrackingOrderLoader();
        var message = new LoadOrderMessage("test-id");

        await loader.LoadAsync(message, cts.Token);

        loader.LastCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region Test helpers

    private sealed record TestOrder(string Id, string OwnerId);

    private sealed record LoadOrderMessage(string OrderId);

    private sealed class TestOrderLoader : ResourceLoaderById<LoadOrderMessage, TestOrder, string>
    {
        private readonly TestOrder? _resource;

        public TestOrderLoader(TestOrder? resource) => _resource = resource;

        protected override string GetId(LoadOrderMessage message) => message.OrderId;

        protected override Task<Result<TestOrder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            _resource is not null
                ? Task.FromResult(Result.Ok(_resource))
                : Task.FromResult(Result.Fail<TestOrder>(Error.NotFound($"Order '{id}' not found.")));
    }

    private sealed class TrackingOrderLoader : ResourceLoaderById<LoadOrderMessage, TestOrder, string>
    {
        public string? LastRequestedId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        protected override string GetId(LoadOrderMessage message) => message.OrderId;

        protected override Task<Result<TestOrder>> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Result.Fail<TestOrder>(Error.NotFound("Not found")));
        }
    }

    #endregion
}
