namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System.IO;
using System.Net;
using Trellis.Testing;

public class HttpResponseHotPathTests
{
    public enum JsonReader { Required, Optional, OptionalOn404 }

    [Theory]
    [InlineData(JsonReader.Required, true)]
    [InlineData(JsonReader.Required, false)]
    [InlineData(JsonReader.Optional, true)]
    [InlineData(JsonReader.Optional, false)]
    [InlineData(JsonReader.OptionalOn404, true)]
    [InlineData(JsonReader.OptionalOn404, false)]
    public void ReadJson_AllReaders_LargeBody_DoesNotAllocateAnotherPayload(JsonReader reader, bool prebuffered)
    {
        const int bodyLength = 256 * 1024;
        var bytes = MakeBody(bodyLength);

        var allocation = BytesPerRead(
            () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = prebuffered ? new ByteArrayContent(bytes) : new ChunkedContent(bytes),
                };
                if (prebuffered)
                    response.Content.LoadIntoBufferAsync().GetAwaiter().GetResult();
                return response;
            },
            response => Read(reader, response, CancellationToken.None),
            AssertPerson);

        allocation.Should().BeLessThan(bodyLength / 4);
    }

    [Fact]
    public void ReadJsonAsync_Prebuffered_VeryLargeBody_UsesAnExactTransientCopy()
    {
        const int bodyLength = 1536 * 1024;
        var bytes = MakeBody(bodyLength);
        var allocation = BytesPerRead(
            () =>
            {
                var content = new ByteArrayContent(bytes);
                content.LoadIntoBufferAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            },
            static response => Read(JsonReader.Required, response, CancellationToken.None),
            AssertPerson);

        allocation.Should().BeInRange(bodyLength, bodyLength + (16 * 1024));
    }

    [Fact]
    public void ReadJsonAsync_Unbuffered_LargeBodyWithKnownLength_AllocatesOneTransientBodyBuffer()
    {
        const int bodyLength = 1536 * 1024;
        var bytes = MakeBody(bodyLength);
        var allocation = BytesPerRead(
            () => CreateChunkedResponse(bytes, bodyLength),
            static response => Read(JsonReader.Required, response, CancellationToken.None),
            AssertPerson);

        allocation.Should().BeInRange(bodyLength, bodyLength + (16 * 1024));
    }

    [Fact]
    public void ReadJsonAsync_Unbuffered_LargeBodyWithUnderstatedLength_DoesNotDoubleTheAdvertisedSize()
    {
        const int advertisedLength = 1536 * 1024;
        const int allocationBudget = 4 * 1024 * 1024;
        var bytes = MakeBody(advertisedLength + (16 * 1024));
        var allocation = BytesPerRead(
            () => CreateChunkedResponse(bytes, advertisedLength),
            static response => Read(JsonReader.Required, response, CancellationToken.None),
            AssertPerson);

        allocation.Should().BeLessThan(allocationBudget,
            "doubling the advertised 1.5 MiB buffer would allocate 4.5 MiB across the two buffers");
    }

    [Fact]
    public async Task ReadJsonAsync_Unbuffered_LargeBodyWithUnknownLength_PreservesDataAcrossBufferGrowth()
    {
        var response = CreateChunkedResponse(MakeBody(1536 * 1024), contentLength: null);

        AssertPerson(await Task.FromResult(Result.Ok<HttpResponseMessage>(response))
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, TestContext.Current.CancellationToken));
        response.Disposed.Should().BeTrue();
    }

    [Fact]
    public void ReadJsonAsync_Unbuffered_SmallBodyWithOverstatedLength_DoesNotReserveTheAdvertisedSize()
    {
        var bytes = MakeBody(4096);
        var allocation = BytesPerRead(
            () => CreateChunkedResponse(bytes, contentLength: 64 * 1024 * 1024),
            static response => Read(JsonReader.Required, response, CancellationToken.None),
            AssertPerson);

        allocation.Should().BeLessThan(64 * 1024);
    }

    [Fact]
    public async Task ReadJsonMaybeAsync_OptionalBody_ContentClosesTheDestination_ThrowsAndDisposesTheResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = new TrackingHttpResponseMessage(HttpStatusCode.OK) { Content = new ClosingWriterContent() };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(JsonReader.Optional, response, ct));

        response.Disposed.Should().BeTrue();
    }

    [Fact]
    public void ReadJsonOrNoneOn404Async_OptionalResource_NoContent_DoesNotAddIntermediateTasks()
    {
        var optional = BytesPerRead(
            static () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
            static response => response.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson),
            static completed => AssertNone(completed));
        var standard = BytesPerRead(
            static () => Task.FromResult(Result.Ok(new HttpResponseMessage(HttpStatusCode.NoContent))),
            static response => response.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson),
            static completed => AssertNone(completed));

        (optional - standard).Should().BeLessThan(32);
    }

    [Fact]
    public void ToResultAsync_Strict_Unauthorized_AllocatesLikeTheSameCustomMapping()
    {
        var strict = BytesPerRead(
            static () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            static response => response.ToResultAsync(),
            static completed => AssertUnauthorized(completed));
        var custom = BytesPerRead(
            static () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            static response => response.ToResultAsync(static status => new Error.AuthenticationRequired
            {
                Detail = $"HTTP response returned status code {(int)status} ({status}).",
            }),
            static completed => AssertUnauthorized(completed));

        (strict - custom).Should().BeLessThan(32);
    }

    [Theory]
    [InlineData(JsonReader.Required)]
    [InlineData(JsonReader.Optional)]
    [InlineData(JsonReader.OptionalOn404)]
    public async Task ReadJson_AllReaders_IncompleteTransportFailure_PropagatesInsteadOfMappingPartialJson(JsonReader reader)
    {
        var response = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ChunkedContent("!"u8.ToArray(), failAfterWrite: true),
        };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Read(reader, response, TestContext.Current.CancellationToken));

        error.InnerException.Should().BeOfType<IOException>();
        response.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ReadJsonAsync_BufferedOrUnbuffered_OversizedContentLength_OnlyRejectsUnbufferedContent(bool prebuffered, bool exceedsIntMax)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = new TrackingHttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        if (prebuffered)
            await response.Content.LoadIntoBufferAsync(ct);
        response.Content.Headers.ContentLength = exceedsIntMax ? (long)int.MaxValue + 1 : (long)Array.MaxLength + 1;

        var reading = Task.FromResult(Result.Ok<HttpResponseMessage>(response))
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, ct);
        if (prebuffered)
        {
            (await reading).Should().BeSuccess();
        }
        else
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => reading);
            error.HttpRequestError.Should().Be(HttpRequestError.ConfigurationLimitExceeded);
        }

        response.Disposed.Should().BeTrue();
    }

    private static Task Read(JsonReader reader, HttpResponseMessage response, CancellationToken ct) => reader switch
    {
        JsonReader.Required => Task.FromResult(Result.Ok(response)).ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, ct),
        JsonReader.Optional => Task.FromResult(Result.Ok(response)).ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, ct),
        JsonReader.OptionalOn404 => Task.FromResult(response).ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(reader)),
    };

    private static byte[] MakeBody(int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)' ');
        """{"firstName":"Ada","""u8.CopyTo(bytes);
        ReadOnlySpan<byte> tail = "\"age\":37}"u8;
        tail.CopyTo(bytes.AsSpan(length - tail.Length));
        return bytes;
    }

    private static TrackingHttpResponseMessage CreateChunkedResponse(byte[] bytes, long? contentLength)
    {
        var content = new ChunkedContent(bytes);
        content.Headers.ContentLength = contentLength;
        return new TrackingHttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static void AssertPerson(Task completed)
    {
        var person = completed is Task<Result<camelcasePerson>> required
            ? required.Result.Should().BeSuccess().Which
            : ((Task<Result<Maybe<camelcasePerson>>>)completed).Result.Should().BeSuccess().Which.Should().HaveValue().Which;
        AssertPerson(person);
    }

    private static void AssertPerson(Result<camelcasePerson> result) => AssertPerson(result.Should().BeSuccess().Which);

    private static void AssertPerson(camelcasePerson person)
    {
        person.firstName.Should().Be("Ada");
        person.age.Should().Be(37);
    }

    private static void AssertNone(Task completed) =>
        ((Task<Result<Maybe<camelcasePerson>>>)completed).Result.Should().HaveValue(Maybe<camelcasePerson>.None);

    private static void AssertUnauthorized(Task completed) =>
        ((Task<Result<HttpResponseMessage>>)completed).Result.Should().BeFailureOfType<Error.AuthenticationRequired>();

    private static long BytesPerRead<T>(Func<T> prepare, Func<T, Task> read, Action<Task> verify)
    {
        const int warmup = 64;
        const int iterations = 64;

        long allocated = 0;
        Task completed = Task.CompletedTask;
        for (var i = -warmup; i < iterations; i++)
        {
            var input = prepare();
            var before = GC.GetAllocatedBytesForCurrentThread();
            completed = read(input);
            if (!completed.IsCompletedSuccessfully)
                throw new InvalidOperationException("An allocation sample must complete on the measuring thread.");
            if (i >= 0)
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        verify(completed);
        return allocated / iterations;
    }

    private sealed class ChunkedContent(byte[] body, bool failAfterWrite = false) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            const int chunkSize = 16 * 1024;
            for (var offset = 0; offset < body.Length; offset += chunkSize)
                await stream.WriteAsync(body.AsMemory(offset, Math.Min(chunkSize, body.Length - offset)), cancellationToken);

            if (failAfterWrite)
                throw new IOException("The response body was truncated.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ClosingWriterContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("""{"firstName":"Ada","age":37}""");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
