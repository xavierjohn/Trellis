namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System.Net;
using System.Text.Json.Serialization;

/// <summary>
/// Test DTO. camelCase property names mirror the legacy v1 fixture.
/// </summary>
#pragma warning disable IDE1006 // Naming Styles (intentional for JSON casing fixture)
public class camelcasePerson
#pragma warning restore IDE1006
{
    public string firstName { get; set; } = string.Empty;
    public int age { get; set; }
}

[JsonSerializable(typeof(camelcasePerson))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

/// <summary>
/// HttpResponseMessage subclass that records when Dispose has been invoked. Tests use this
/// to assert that Trellis.Http extensions fulfil their disposal contract on terminal paths.
/// </summary>
internal sealed class TrackingHttpResponseMessage(HttpStatusCode statusCode) : HttpResponseMessage(statusCode)
{
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}