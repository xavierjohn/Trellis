namespace Trellis.Testing.Tests;

public class CanceledTokenTests
{
    [Fact]
    public void Create_ReturnsAlreadyCanceledToken()
    {
        using var canceled = CanceledToken.Create();

        canceled.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Create_EachCall_ProducesDistinctTokenIdentity()
    {
        using var first = CanceledToken.Create();
        using var second = CanceledToken.Create();

        first.Token.Should().NotBe(second.Token);
        first.Token.Should().Be(first.Token);
    }

    [Fact]
    public void ToTask_ReturnsCanceledTasksCarryingTheToken()
    {
        using var canceled = CanceledToken.Create();

        canceled.ToTask().IsCanceled.Should().BeTrue();
        canceled.ToTask<int>().IsCanceled.Should().BeTrue();
        canceled.ToValueTask().AsTask().IsCanceled.Should().BeTrue();
        canceled.ToValueTask<int>().AsTask().IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task ToTask_AwaitedFailure_CarriesTheSameToken()
    {
        using var canceled = CanceledToken.Create();
        var act = async () => await canceled.ToTask<int>();

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(canceled.Token);
    }

    [Fact]
    public void Dispose_ReleasesTheSource_AndIsIdempotent()
    {
        var canceled = CanceledToken.Create();

        canceled.Dispose();
        var again = canceled.Dispose;

        again.Should().NotThrow();
    }

    [Fact]
    public void Token_AfterDispose_RemainsUsableAndCanceled()
    {
        var canceled = CanceledToken.Create();
        var token = canceled.Token;

        canceled.Dispose();

        canceled.Token.Should().Be(token);
        canceled.Token.IsCancellationRequested.Should().BeTrue();
        canceled.ToTask().IsCanceled.Should().BeTrue();
    }
}