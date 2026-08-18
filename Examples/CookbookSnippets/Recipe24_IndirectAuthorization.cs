// Cookbook Recipe 24 — Indirect (multi-hop) resource authorization.
//
// Each scenario lives in its own namespace: the leaf-to-owner path resolver rejects
// multiple distinct paths, so Match must declare exactly one IIdentifyRelatedResource[s]
// declaration per scenario.

// ---------------------------------------------------------------------------
// Cricket fan-out — Match → {HomeTeam, AwayTeam}, OR-ownership.
// ---------------------------------------------------------------------------
namespace CookbookSnippets.Recipe24.FanOut
{
    using System.Collections.Generic;
    using System.Linq;
    using global::Mediator;
    using Microsoft.Extensions.DependencyInjection;
    using Trellis;
    using Trellis.Authorization;
    using Trellis.Mediator;

    public sealed partial class MatchId : RequiredGuid<MatchId>;

    public sealed partial class TeamId : RequiredGuid<TeamId>;

    public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResources<Team, TeamId>
    {
        public Match(MatchId id, TeamId homeTeamId, TeamId awayTeamId) : base(id)
        {
            HomeTeamId = homeTeamId;
            AwayTeamId = awayTeamId;
        }

        public TeamId HomeTeamId { get; }

        public TeamId AwayTeamId { get; }

        public IReadOnlyList<TeamId> GetRelatedResourceIds() => [HomeTeamId, AwayTeamId];
    }

    public sealed class Team : Aggregate<TeamId>
    {
        public Team(TeamId id, ActorId createdByActorId) : base(id) =>
            CreatedByActorId = createdByActorId;

        public ActorId CreatedByActorId { get; }
    }

    public sealed record UploadScorecardCommand(MatchId MatchId, string Scorecard)
        : ICommand<Result<Trellis.Unit>>,
          IAuthorizeResourceVia<Team>,
          IIdentifyResource<Match, MatchId>
    {
        public MatchId GetResourceId() => MatchId;

        public Trellis.IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
            Result.Ensure(
                owners.Any(t => t.CreatedByActorId == actor.Id),
                new Error.Forbidden("match.upload-scorecard")
                { Detail = "Actor does not own either match team." });
    }

    public static class FanOutWiring
    {
        // Composition root — assembly scan registers everything.
        public static IServiceCollection Wire(IServiceCollection services)
        {
            services.AddTrellisBehaviors();
            services.AddResourceAuthorization(typeof(UploadScorecardCommand).Assembly);
            return services;
        }
    }
}

// ---------------------------------------------------------------------------
// Chain — Match → Team → Tournament. Singular chains always pass a list of size 1.
// ---------------------------------------------------------------------------
namespace CookbookSnippets.Recipe24.Chain
{
    using System.Collections.Generic;
    using global::Mediator;
    using Trellis;
    using Trellis.Authorization;
    using Trellis.Mediator;

    public sealed partial class MatchId : RequiredGuid<MatchId>;

    public sealed partial class TeamId : RequiredGuid<TeamId>;

    public sealed partial class TournamentId : RequiredGuid<TournamentId>;

    public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResource<Team, TeamId>
    {
        public Match(MatchId id, TeamId teamId) : base(id) => TeamId = teamId;

        public TeamId TeamId { get; }

        public TeamId GetRelatedResourceId() => TeamId;
    }

    public sealed class Team : Aggregate<TeamId>, IIdentifyRelatedResource<Tournament, TournamentId>
    {
        public Team(TeamId id, TournamentId tournamentId) : base(id) => TournamentId = tournamentId;

        public TournamentId TournamentId { get; }

        public TournamentId GetRelatedResourceId() => TournamentId;
    }

    public sealed class Tournament : Aggregate<TournamentId>
    {
        public Tournament(TournamentId id, ActorId ownerActorId) : base(id) =>
            OwnerActorId = ownerActorId;

        public ActorId OwnerActorId { get; }
    }

    public sealed record CancelMatchCommand(MatchId MatchId)
        : ICommand<Result<Trellis.Unit>>,
          IAuthorizeResourceVia<Tournament>,
          IIdentifyResource<Match, MatchId>
    {
        public MatchId GetResourceId() => MatchId;

        public Trellis.IResult Authorize(Actor actor, IReadOnlyList<Tournament> owners) =>
            Result.Ensure(
                owners[0].OwnerActorId == actor.Id,
                new Error.Forbidden("match.cancel"));
    }
}

// ---------------------------------------------------------------------------
// AOT / explicit registration — single-hop overload (no fan-out, no chains).
// ---------------------------------------------------------------------------
namespace CookbookSnippets.Recipe24.ExplicitRegistration
{
    using System.Collections.Generic;
    using global::Mediator;
    using Microsoft.Extensions.DependencyInjection;
    using Trellis;
    using Trellis.Authorization;
    using Trellis.Mediator;

    public sealed partial class MatchId : RequiredGuid<MatchId>;

    public sealed partial class TeamId : RequiredGuid<TeamId>;

    public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResource<Team, TeamId>
    {
        public Match(MatchId id, TeamId teamId) : base(id) => TeamId = teamId;

        public TeamId TeamId { get; }

        public TeamId GetRelatedResourceId() => TeamId;
    }

    public sealed class Team : Aggregate<TeamId>
    {
        public Team(TeamId id, ActorId createdByActorId) : base(id) =>
            CreatedByActorId = createdByActorId;

        public ActorId CreatedByActorId { get; }
    }

    public sealed record DeleteMatchCommand(MatchId MatchId)
        : ICommand<Result<Trellis.Unit>>,
          IAuthorizeResourceVia<Team>,
          IIdentifyResource<Match, MatchId>
    {
        public MatchId GetResourceId() => MatchId;

        public Trellis.IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
            Result.Ensure(
                owners[0].CreatedByActorId == actor.Id,
                new Error.Forbidden("match.delete"));
    }

    public static class ExplicitWiring
    {
        public static IServiceCollection Wire(IServiceCollection services) =>
            services.AddRelatedResourceAuthorization<
                DeleteMatchCommand, Match, MatchId, Team, TeamId, Result<Trellis.Unit>>(
                extractOwnerId: match => match.TeamId);  // single-hop selector
    }
}