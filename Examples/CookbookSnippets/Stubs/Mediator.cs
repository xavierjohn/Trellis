// Cross-recipe mediator messages used by Recipes 4 and 5.
namespace CookbookSnippets.Stubs;

using CookbookSnippets.Recipe01;
using global::Mediator;
using Trellis;

public sealed record GetOrderQuery(System.Guid Id) : IQuery<Result<Order>>;