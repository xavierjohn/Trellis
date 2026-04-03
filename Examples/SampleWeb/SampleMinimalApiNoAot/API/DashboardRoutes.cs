namespace SampleMinimalApiNoAot.API;

using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using Trellis;

public record DashboardResponse(int ProductCount, int OrderCount, decimal TotalRevenue);

public static class DashboardRoutes
{
    public static void UseDashboardRoute(this WebApplication app) =>
        // GET /dashboard — concurrent data fetching
        // Demonstrates: ParallelAsync / WhenAllAsync for performance
        // Uses IDbContextFactory for separate contexts per parallel task (DbContext is not thread-safe)
        // Uses Result.TryAsync to convert EF Core exceptions into Result failures
        app.MapGet("/dashboard", async (IDbContextFactory<AppDbContext> dbFactory) =>
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
                                .Where(o => o.State == SampleUserLibrary.OrderState.Confirmed
                                         || o.State == SampleUserLibrary.OrderState.Shipped
                                         || o.State == SampleUserLibrary.OrderState.Delivered)
                                .SelectMany(o => o.Lines)
                                .SumAsync(l => l.UnitPrice.Value * l.Quantity));
                    })
                .WhenAllAsync()
                .MapAsync((productCount, orderCount, revenue) =>
                    new DashboardResponse(productCount, orderCount, revenue));

            return result.Match(
                onSuccess: dashboard => Results.Ok(dashboard),
                onFailure: error => Results.Problem(error.Detail));
        });
}
