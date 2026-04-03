namespace SampleDataAccess;

using Microsoft.EntityFrameworkCore;
using SampleUserLibrary;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

/// <summary>
/// Seeds initial data for the sample database.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Products.AnyAsync())
            return;

        var products = new[]
        {
            Product.TryCreate(ProductName.Create("Mechanical Keyboard"), MonetaryAmount.Create(149.99m), 50),
            Product.TryCreate(ProductName.Create("Wireless Mouse"), MonetaryAmount.Create(79.99m), 100),
            Product.TryCreate(ProductName.Create("USB-C Hub"), MonetaryAmount.Create(59.99m), 75),
            Product.TryCreate(ProductName.Create("Monitor Stand"), MonetaryAmount.Create(39.99m), 200),
            Product.TryCreate(ProductName.Create("Laptop Backpack"), MonetaryAmount.Create(89.99m), 30),
            Product.TryCreate(ProductName.Create("Noise Cancelling Headphones"), MonetaryAmount.Create(299.99m), 25),
            Product.TryCreate(ProductName.Create("Webcam HD"), MonetaryAmount.Create(69.99m), 60),
            Product.TryCreate(ProductName.Create("Desk Lamp"), MonetaryAmount.Create(44.99m), 150),
        };

        foreach (var result in products)
        {
            result.Tap(product => context.Products.Add(product));
        }

        await context.SaveChangesResultUnitAsync();
    }
}
