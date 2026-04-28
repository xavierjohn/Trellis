namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// MVC adapter that wraps an <see cref="Microsoft.AspNetCore.Http.IResult"/> in an
/// <see cref="ActionResult{T}"/> so MVC controllers can declare typed return signatures
/// (e.g. <c>Task&lt;ActionResult&lt;TodoResponse&gt;&gt;</c>) for OpenAPI inference and
/// <c>[ProducesResponseType&lt;T&gt;]</c> compatibility.
/// </summary>
/// <remarks>
/// This is a thin opt-in adapter. The wrapped <see cref="Microsoft.AspNetCore.Http.IResult"/>
/// owns all the protocol logic; the adapter only forwards
/// <see cref="ActionResult.ExecuteResultAsync(ActionContext)"/> to
/// <see cref="Microsoft.AspNetCore.Http.IResult.ExecuteAsync(HttpContext)"/>.
/// </remarks>
public static class ActionResultAdapterExtensions
{
    /// <summary>
    /// Wraps an <see cref="Microsoft.AspNetCore.Http.IResult"/> in an <see cref="ActionResult{T}"/>
    /// for MVC controllers that want a typed return signature.
    /// </summary>
    public static ActionResult<T> AsActionResult<T>(this Microsoft.AspNetCore.Http.IResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TrellisActionResult<T>(result);
    }

    /// <summary>
    /// Wraps a <see cref="Task{IResult}"/> in an <see cref="ActionResult{T}"/>.
    /// </summary>
    public static async Task<ActionResult<T>> AsActionResultAsync<T>(this Task<Microsoft.AspNetCore.Http.IResult> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.AsActionResult<T>();
    }

    /// <summary>
    /// Wraps a <see cref="ValueTask{IResult}"/> in an <see cref="ActionResult{T}"/>.
    /// </summary>
    public static async ValueTask<ActionResult<T>> AsActionResultAsync<T>(this ValueTask<Microsoft.AspNetCore.Http.IResult> resultTask)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.AsActionResult<T>();
    }
}

/// <summary>
/// Internal MVC <see cref="ActionResult{T}"/> wrapper. Forwards execution to the inner
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> using the controller's <see cref="HttpContext"/>.
/// Implements <see cref="Microsoft.AspNetCore.Mvc.Infrastructure.IConvertToActionResult"/> so the
/// MVC return-type machinery treats it as the underlying <see cref="ActionResult{T}"/>.
/// </summary>
internal sealed class TrellisActionResult<T> : ActionResult, Microsoft.AspNetCore.Mvc.Infrastructure.IConvertToActionResult
{
    private readonly Microsoft.AspNetCore.Http.IResult _inner;

    public TrellisActionResult(Microsoft.AspNetCore.Http.IResult inner) => _inner = inner;

    /// <summary>The wrapped <see cref="Microsoft.AspNetCore.Http.IResult"/> (exposed for testing).</summary>
    public Microsoft.AspNetCore.Http.IResult Inner => _inner;

    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _inner.ExecuteAsync(context.HttpContext);
    }

    /// <summary>
    /// MVC calls this when an action signature uses <c>ActionResult&lt;T&gt;</c>; we return
    /// ourselves (the IResult-wrapping ActionResult) so MVC will execute us via
    /// <see cref="ExecuteResultAsync(ActionContext)"/>.
    /// </summary>
    public IActionResult Convert() => this;
}