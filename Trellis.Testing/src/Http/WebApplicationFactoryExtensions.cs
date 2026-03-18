namespace Trellis.Testing;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Authorization;

/// <summary>
/// Extension methods for <see cref="WebApplicationFactory{TEntryPoint}"/>
/// that simplify creating authenticated HTTP clients for integration tests.
/// </summary>
public static class WebApplicationFactoryExtensions
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the <c>X-Test-Actor</c> header pre-set,
    /// encoding the specified actor identity and permissions as JSON.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="actorId">The unique identifier of the test actor.</param>
    /// <param name="permissions">The permissions granted to the test actor.</param>
    /// <returns>An <see cref="HttpClient"/> with the actor header configured.</returns>
    /// <example>
    /// <code>
    /// var client = _factory.CreateClientWithActor("user-1", Permissions.OrdersCreate, Permissions.OrdersRead);
    /// var response = await client.PostAsync("/api/orders", content);
    /// response.StatusCode.Should().Be(HttpStatusCode.OK);
    /// </code>
    /// </example>
    public static HttpClient CreateClientWithActor<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        string actorId,
        params string[] permissions)
        where TEntryPoint : class
    {
        permissions ??= [];

        var client = factory.CreateClient();
        var json = new JsonObject
        {
            ["Id"] = actorId,
            ["Permissions"] = new JsonArray(permissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["ForbiddenPermissions"] = new JsonArray(),
            ["Attributes"] = new JsonObject()
        }.ToJsonString();
        client.DefaultRequestHeaders.Add("X-Test-Actor", json);
        return client;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the <c>X-Test-Actor</c> header pre-set,
    /// encoding the full <see cref="Actor"/> (including forbidden permissions and attributes) as JSON.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="actor">The actor to serialize into the header.</param>
    /// <returns>An <see cref="HttpClient"/> with the actor header configured.</returns>
    public static HttpClient CreateClientWithActor<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        Actor actor)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(actor);

        var client = factory.CreateClient();
        var json = new JsonObject
        {
            ["Id"] = actor.Id,
            ["Permissions"] = new JsonArray(actor.Permissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["ForbiddenPermissions"] = new JsonArray(actor.ForbiddenPermissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["Attributes"] = new JsonObject(actor.Attributes.Select(kvp => new KeyValuePair<string, JsonNode?>(kvp.Key, JsonValue.Create(kvp.Value))).ToList())
        }.ToJsonString();
        client.DefaultRequestHeaders.Add("X-Test-Actor", json);
        return client;
    }
}