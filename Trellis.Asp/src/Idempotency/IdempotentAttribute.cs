namespace Trellis.Asp.Idempotency;

using System;

/// <summary>
/// Opts an endpoint or controller in to <c>IdempotencyMiddleware</c> processing of the
/// <c>Idempotency-Key</c> request header (IETF draft <c>draft-ietf-httpapi-idempotency-key-header</c>).
/// </summary>
/// <remarks>
/// <para>
/// Endpoints without this attribute pass through the middleware with zero overhead. Apply at the
/// method level for fine-grained opt-in, or at the class / controller level to opt every action
/// in by default. The attribute is read from <c>Endpoint.Metadata</c> at request time, so both
/// MVC and Minimal-API endpoints honour it.
/// </para>
/// <para>
/// The attribute is not inherited: endpoint metadata reflects the closest declaration that
/// produced the endpoint, and silently propagating idempotency processing to derived controllers
/// is a security and correctness footgun (the derived controller may not have been audited for
/// safe replay).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpPost("orders")]
/// [Idempotent]
/// public Task&lt;IActionResult&gt; CreateOrder([FromBody] CreateOrderRequest request) =&gt; ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IdempotentAttribute : Attribute
{
}