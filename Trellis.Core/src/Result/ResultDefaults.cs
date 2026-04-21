namespace Trellis;

/// <summary>
/// Internal default-state sentinel for <see cref="Result"/> and <see cref="Result{TValue}"/>.
/// Single shared allocation across all closed generic instantiations of <see cref="Result{TValue}"/>.
/// Per ADR-002 §3.5.1, <c>default(Result)</c> and <c>default(Result&lt;T&gt;)</c> are observationally
/// equivalent to <c>Result.Fail(<see cref="Sentinel"/>)</c> / <c>Result.Fail&lt;T&gt;(<see cref="Sentinel"/>)</c>.
/// </summary>
internal static class ResultDefaults
{
    /// <summary>
    /// The single shared <see cref="Trellis.Error.Unexpected"/> instance returned by
    /// <see cref="Result.Error"/> / <see cref="Result{TValue}.Error"/> when the result was default-initialized.
    /// </summary>
    internal static readonly Error Sentinel = new Error.Unexpected("default_initialized")
    {
        Detail = "Result was default-initialized; use Result.Ok(...) or Result.Fail(...) instead.",
    };
}
