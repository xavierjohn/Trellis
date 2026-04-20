namespace SampleMinimalApi.Models;

using SampleUserLibrary;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Wire DTO for creating a product. Uses scalar VOs directly so JSON deserialization
/// fails fast with a 422 + FieldViolations when any field is invalid (axiom A1a).
/// </summary>
public sealed record CreateProductDto
{
    public required ProductName Name { get; init; }
    public required MonetaryAmount Price { get; init; }
    public required int StockQuantity { get; init; }
}

/// <summary>
/// Wire DTO for creating a draft order.
/// </summary>
public sealed record CreateOrderRequest
{
    public required CustomerId CustomerId { get; init; }
}

/// <summary>
/// Wire DTO for adding a line to an existing order.
/// </summary>
public sealed record AddLineDto
{
    public required ProductId ProductId { get; init; }
    public required int Quantity { get; init; }
}
