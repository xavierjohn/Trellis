// Cookbook Recipe 31 — Avoid duplicate load with IAuthorizedResource<TCommand, TResource>.
namespace CookbookSnippets.Recipe31;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Trellis;
using Trellis.Authorization;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }

    public bool IsCancelled { get; private set; }

    public void Cancel() => IsCancelled = true;
}

public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Trellis.Unit>>,
      IAuthorizeResource<Order>,
      IIdentifyResource<Order, OrderId>
{
    public OrderId GetResourceId() => OrderId;

    public Trellis.IResult Authorize(Actor actor, Order resource) =>
        Result.Ensure(actor.Permissions.Contains("orders:cancel"), new Error.Forbidden("orders.cancel-denied"));
}

// The handler reads the instance the pipeline already loaded to run Authorize — no second
// roundtrip. The framework guarantees identity, not mutation-readiness.
public sealed class CancelOrderHandler(IAuthorizedResource<CancelOrderCommand, Order> authorized)
    : ICommandHandler<CancelOrderCommand, Result<Trellis.Unit>>
{
    public ValueTask<Result<Trellis.Unit>> Handle(CancelOrderCommand cmd, CancellationToken cancellationToken)
    {
        authorized.GetRequiredResource().Cancel();
        return new(Result.Ok(Trellis.Unit.Value));
    }
}

public sealed partial class MatchId : RequiredGuid<MatchId>;

public sealed class Scorecard : ValueObject
{
    public Scorecard(int runs) => Runs = runs;

    public int Runs { get; }

    protected override void GetEqualityComponents(ref EqualityComponents components)
        => components.Add(Runs);
}

public sealed partial class TeamId : RequiredGuid<TeamId>;

public sealed class Team : Aggregate<TeamId>
{
    public Team(TeamId id, ActorId createdByActorId) : base(id) => CreatedByActorId = createdByActorId;

    public ActorId CreatedByActorId { get; }
}

public sealed class Match : Aggregate<MatchId>
{
    public Match(MatchId id) : base(id) { }

    public Scorecard? Scorecard { get; private set; }

    public void UploadScorecard(Scorecard scorecard) => Scorecard = scorecard;
}

// Via commands authorize through an owner but expose the LEAF through the accessor — the
// resource the message identifies, which is the typical mutation target.
public sealed record UploadScorecardCommand(MatchId MatchId, Scorecard Scorecard)
    : ICommand<Result<Trellis.Unit>>,
      IIdentifyResource<Match, MatchId>,
      IAuthorizeResourceVia<Team>
{
    public MatchId GetResourceId() => MatchId;

    public Trellis.IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
        Result.Ensure(owners.Any(t => t.CreatedByActorId == actor.Id), new Error.Forbidden("not_team_owner"));
}

public sealed class UploadScorecardHandler(IAuthorizedResource<UploadScorecardCommand, Match> match)
    : ICommandHandler<UploadScorecardCommand, Result<Trellis.Unit>>
{
    public ValueTask<Result<Trellis.Unit>> Handle(UploadScorecardCommand cmd, CancellationToken cancellationToken)
    {
        match.GetRequiredResource().UploadScorecard(cmd.Scorecard);
        return new(Result.Ok(Trellis.Unit.Value));
    }
}

internal static class Recipe31Demonstrator
{
    // TryGetResource is the non-throwing read for optional access.
    public static bool OptionalRead(IAuthorizedResource<CancelOrderCommand, Order> accessor)
        => accessor.TryGetResource(out Order? order) && order is not null;
}

#if FALSE
// Wrong — the handler reloads the resource the pipeline already loaded.
// public sealed class CancelOrderHandler(IOrderRepository orders)
//     : ICommandHandler<CancelOrderCommand, Result<Unit>>
// {
//     public async ValueTask<Result<Unit>> Handle(CancelOrderCommand cmd, CancellationToken cancellationToken)
//     {
//         var found = await orders.FindByIdAsync(cmd.OrderId, ct);   // SECOND lookup — wasteful
//         ...
//     }
// }
#endif
