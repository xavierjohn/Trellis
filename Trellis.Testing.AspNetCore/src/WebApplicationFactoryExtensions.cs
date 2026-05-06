namespace Trellis.Testing.AspNetCore;

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
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
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(actorId);
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
        ArgumentNullException.ThrowIfNull(factory);
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

    /// <summary>
    /// Creates an <see cref="HttpClient"/> authenticated with a real Azure Entra ID token
    /// acquired via MSAL ROPC flow. Use for E2E integration tests against a real Entra test tenant.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="tokenProvider">
    /// The MSAL token provider configured with test tenant credentials.
    /// Create one instance per test class/fixture to benefit from token caching.
    /// </param>
    /// <param name="testUserName">
    /// The key in <see cref="MsalTestOptions.TestUsers"/> identifying the test user.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="HttpClient"/> with the Authorization Bearer header set.</returns>
    /// <example>
    /// <code>
    /// var tokenProvider = new MsalTestTokenProvider(msalOptions);
    /// var client = await _factory.CreateClientWithEntraTokenAsync(tokenProvider, "salesRep");
    /// var response = await client.PostAsync("/api/orders", content);
    /// response.StatusCode.Should().Be(HttpStatusCode.OK);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("MSAL uses reflection for token serialization and is not AOT-compatible.")]
    public static async Task<HttpClient> CreateClientWithEntraTokenAsync<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        MsalTestTokenProvider tokenProvider,
        string testUserName,
        CancellationToken cancellationToken = default)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(testUserName);

        var token = await tokenProvider.AcquireTokenAsync(testUserName, cancellationToken)
            .ConfigureAwait(false);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}