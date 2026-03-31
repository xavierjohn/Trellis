namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// RFC 9110 §15.4 redirect result helpers for MVC controllers.
/// </summary>
public static class RedirectResultExtensions
{
    /// <summary>301 Moved Permanently — resource has been permanently moved.</summary>
    public static ActionResult ToMovedPermanently(this ControllerBase controller, string uri) =>
        controller.RedirectPermanent(uri);

    /// <summary>302 Found — temporary redirect.</summary>
    public static ActionResult ToFound(this ControllerBase controller, string uri) =>
        controller.Redirect(uri);

    /// <summary>303 See Other — redirect after POST (always GET the new URI).</summary>
    public static ActionResult ToSeeOther<TValue>(this Result<TValue> result, ControllerBase controller, Func<TValue, string> uriSelector)
    {
        if (result.IsFailure)
            return result.Error.ToActionResult<TValue>(controller).Result!;

        var uri = uriSelector(result.Value);
        controller.Response.Headers.Location = uri;
        return new StatusCodeResult(StatusCodes.Status303SeeOther);
    }

    /// <summary>307 Temporary Redirect — temporary redirect preserving method.</summary>
    public static ActionResult ToTemporaryRedirect(this ControllerBase controller, string uri) =>
        controller.RedirectPreserveMethod(uri);

    /// <summary>308 Permanent Redirect — permanent redirect preserving method.</summary>
    public static ActionResult ToPermanentRedirect(this ControllerBase controller, string uri) =>
        controller.RedirectPermanentPreserveMethod(uri);
}
