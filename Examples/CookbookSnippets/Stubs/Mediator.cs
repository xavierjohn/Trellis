// Cross-recipe mediator messages used by Recipes 4 and 5.
namespace CookbookSnippets.Stubs;

using global::Mediator;
using Trellis;
using CookbookSnippets.Recipe01;

public sealed record GetOrderQuery(System.Guid Id) : IQuery<Result<Order>>;
