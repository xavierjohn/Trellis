namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;

public record DashboardResponse(int ProductCount, int OrderCount, decimal TotalRevenue);

[ApiController]
[Route("[controller]")]
public class DashboardController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    // GET /dashboard — concurrent data fetching
    // Demonstrates: ParallelAsync / WhenAllAsync for performance
    // Uses IDbContextFactory for separate contexts per parallel task (DbContext is not thread-safe)
    // Uses Result.TryAsync to convert EF Core exceptions into Result failures
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get()
    {
        var result = await Result.ParallelAsync(
                async () =>
                {
                    await using var db = dbFactory.CreateDbContext();
                    return await Result.TryAsync(() => db.Products.CountAsync());
                },
                async () =>
                {
                    await using var db = dbFactory.CreateDbContext();
                    return await Result.TryAsync(() => db.Orders.CountAsync());
                },
                async () =>
                {
                    await using var db = dbFactory.CreateDbContext();
                    return await Result.TryAsync(() =>
                        db.Orders
                            .Where(o => o.State == OrderState.Confirmed
                                     || o.State == OrderState.Shipped
                                     || o.State == OrderState.Delivered)
                            .SelectMany(o => o.Lines)
                            .SumAsync(l => l.UnitPrice.Value * l.Quantity));
                })
            .WhenAllAsync()
            .MapAsync((productCount, orderCount, revenue) =>
                new DashboardResponse(productCount, orderCount, revenue));

        return result.ToActionResult(this);
    }
}
