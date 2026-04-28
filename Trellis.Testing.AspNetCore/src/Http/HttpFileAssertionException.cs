namespace Trellis.Testing.AspNetCore.Http;

using System;

/// <summary>
/// Raised by <see cref="HttpFileAssertions"/> when a request's expectations
/// are not met. Carries a descriptive message identifying the offending
/// request by title so failures are easy to triage in CI.
/// </summary>
public sealed class HttpFileAssertionException : Exception
{
    /// <summary>Creates a new instance.</summary>
    public HttpFileAssertionException() : base()
    {
    }

    /// <summary>Creates a new instance with the given message.</summary>
    /// <param name="message">Failure message.</param>
    public HttpFileAssertionException(string message) : base(message)
    {
    }

    /// <summary>Creates a new instance with a message and inner exception.</summary>
    /// <param name="message">Failure message.</param>
    /// <param name="inner">Underlying exception.</param>
    public HttpFileAssertionException(string message, Exception inner) : base(message, inner)
    {
    }
}