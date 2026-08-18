namespace Trellis.Asp.Authorization;

using Trellis.Authorization;

/// <summary>
/// Internal capability that lets the response writer "see through" decorating
/// <see cref="IActorProvider"/> implementations (such as <c>CachingActorProvider</c>) to the
/// underlying provider when emitting diagnostics. Without this, the fail-closed exception
/// raised by <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> would name the
/// decorator type as the offender, and consumers would have to know the decorator's contract
/// to find the real remediation site (their custom inner provider).
/// </summary>
internal interface IDecoratingActorProvider
{
    IActorProvider Inner { get; }
}