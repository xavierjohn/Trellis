// Cookbook Recipe 32 — Hide existence with AuthFailureExposurePolicy.HideAsNotFound.
namespace CookbookSnippets.Recipe32;

using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.ServiceDefaults;

public sealed partial class IncidentId : RequiredString<IncidentId>;

public sealed class Incident : Aggregate<IncidentId>
{
    public Incident(IncidentId id, ActorId assigneeId) : base(id) => AssigneeId = assigneeId;

    public ActorId AssigneeId { get; }
}

public sealed record IncidentDto(string Id);

public sealed record SecurityFinding(string Id);

public sealed record PrivateProfile(string Id);

// Command and loader are unchanged from Recipe 7 — hiding existence is a composition-root policy,
// not a change to the authorization rule.
public sealed record GetIncidentQuery(IncidentId Id)
    : IQuery<Result<IncidentDto>>,
      IAuthorizeResource<Incident>,
      IIdentifyResource<Incident, IncidentId>
{
    public IncidentId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Incident incident) =>
        Result.Ensure(
            incident.AssigneeId == actor.Id || actor.HasPermission("incidents:read-any"),
            new Error.Forbidden("incidents.read-denied"));
}

public sealed class GetIncidentHandler(IAuthorizedResource<GetIncidentQuery, Incident> authorized)
    : IQueryHandler<GetIncidentQuery, Result<IncidentDto>>
{
    public ValueTask<Result<IncidentDto>> Handle(GetIncidentQuery query, CancellationToken cancellationToken)
        => new(Result.Ok(new IncidentDto(authorized.GetRequiredResource().Id.Value)));
}

public static class HideExistenceDi
{
    public static IServiceCollection Wire(IServiceCollection services)
    {
        services.AddTrellis(options => options
            .UseResourceAuthorization()
            .UseResourceAuthorization<GetIncidentQuery, Incident, Result<IncidentDto>>()
            .UseResourceAuthorization(o => o.HideExistence<Incident>()));

        return services;
    }

    // Repeated configure delegates compose against the same options instance in registration
    // order, so these styles produce the same merged policy.
    public static IServiceCollection WireMultipleResources(IServiceCollection services)
    {
        services.AddTrellis(options => options
            .UseResourceAuthorization()
            .UseResourceAuthorization(o => o
                .HideExistence<Incident>()
                .HideExistence<SecurityFinding>()
                .HideExistence<PrivateProfile>()));

        return services;
    }
}
