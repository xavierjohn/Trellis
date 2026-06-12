// Cookbook Recipe 21 — Parallel independent loads in handlers using Result.ParallelAsync + WhenAllAsync.
//
// This snippet illustrates the call-site mechanics (factory shape, WhenAllAsync, fold via Combine).
// The repository interfaces below are deliberately abstract — they do NOT pin a backing store.
// Before using this shape against your own repositories, check Recipe 21's "When NOT to use it":
// two repositories sharing a scoped EF Core DbContext (the typical setup, where the scoped
// lifetime comes from services.AddDbContext<TContext>() — AddTrellisUnitOfWork<TContext>() then
// consumes that scoped context) will race and throw. Safe shapes: HTTP + DB, two distinct
// upstream services, or factory-created independent DbContexts via IDbContextFactory<TContext>.
namespace CookbookSnippets.Recipe21;

using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Trellis;

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class ProductId : RequiredGuid<ProductId>;

public sealed partial class DraftOrderId : RequiredGuid<DraftOrderId>;

public sealed class Customer : Aggregate<CustomerId>
{
    private Customer(CustomerId id) : base(id) { }

    public static Customer ForTesting(CustomerId id) => new(id);
}

public sealed class Product : Aggregate<ProductId>
{
    public int StockOnHand { get; private set; }

    private Product(ProductId id) : base(id) { }

    public static Product ForTesting(ProductId id, int stock) =>
        new(id) { StockOnHand = stock };
}

public sealed class DraftOrder : Aggregate<DraftOrderId>
{
    private DraftOrder(DraftOrderId id) : base(id) { }

    public static Result<DraftOrder> CreateDraft(Customer customer, Product product, int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Fail<DraftOrder>(
                Error.InvalidInput.ForField("quantity", "out_of_range", "Quantity must be positive."));
        }

        if (product.StockOnHand < quantity)
        {
            return Result.Fail<DraftOrder>(
                Error.InvalidInput.ForRule("insufficient_stock", "Not enough stock for the requested quantity."));
        }

        return DraftOrderId.TryCreate(System.Guid.NewGuid())
            .Map(id => new DraftOrder(id));
    }
}

public interface ICustomerRepository
{
    Task<Result<Customer>> FindByIdAsync(CustomerId id, CancellationToken ct);
}

public interface IProductRepository
{
    Task<Result<Product>> FindByIdAsync(ProductId id, CancellationToken ct);
}

public interface IDraftOrderRepository
{
    void Add(DraftOrder order);
}

public sealed record CreateDraftOrderCommand(CustomerId CustomerId, ProductId ProductId, int Quantity)
    : ICommand<Result<DraftOrderId>>;

public sealed class CreateDraftOrderHandler(
    ICustomerRepository customers,
    IProductRepository products,
    IDraftOrderRepository orders) : ICommandHandler<CreateDraftOrderCommand, Result<DraftOrderId>>
{
    public ValueTask<Result<DraftOrderId>> Handle(CreateDraftOrderCommand command, CancellationToken cancellationToken) =>
        new(Result.ParallelAsync(
                //  ↑ takes parameterless factory funcs — NOT pre-started tasks.
                //    Each factory is invoked eagerly here so both loads execute concurrently.
                () => customers.FindByIdAsync(command.CustomerId, cancellationToken),
                () => products.FindByIdAsync(command.ProductId, cancellationToken))
            .WhenAllAsync()
            //  ↑ awaits Task.WhenAll, folds the two Result<T> into Result<(Customer, Product)>
            //    via Result.Combine.
            .BindAsync((customer, product) => DraftOrder.CreateDraft(customer, product, command.Quantity))
            .TapAsync(orders.Add)
            .MapAsync(o => o.Id));
}

#if FALSE
// ❌ Sequential await: latency = customers.Find + products.Find. Tests pass; the bug is
// invisible at the call site because the code "looks" correct.
internal sealed class WrongHandler(
    ICustomerRepository customers,
    IProductRepository products,
    IDraftOrderRepository orders) : ICommandHandler<CreateDraftOrderCommand, Result<DraftOrderId>>
{
    public async ValueTask<Result<DraftOrderId>> Handle(CreateDraftOrderCommand command, CancellationToken cancellationToken)
    {
        var customerResult = await customers.FindByIdAsync(command.CustomerId, cancellationToken);
        var productResult  = await products.FindByIdAsync(command.ProductId, cancellationToken);  // serialised behind customer

        return Result.Combine(customerResult, productResult)
            .Bind(t => DraftOrder.CreateDraft(t.Item1, t.Item2, command.Quantity))
            .Tap(orders.Add)
            .Map(o => o.Id);
    }
}
#endif

internal static class Recipe21ParallelAsyncSurface
{
    public static async Task ThreeFactoryArity()
    {
        var result = await Result.ParallelAsync(
            LoadNumberAsync,
            LoadNameAsync,
            LoadAvailableAsync)
            .WhenAllAsync();

        _ = result;
    }

    public static async Task NineFactoryArity()
    {
        var result = await Result.ParallelAsync(
            LoadNumberAsync,
            LoadNameAsync,
            LoadAvailableAsync,
            LoadNumberAsync,
            LoadNameAsync,
            LoadAvailableAsync,
            LoadNumberAsync,
            LoadNameAsync,
            LoadAvailableAsync)
            .WhenAllAsync();

        _ = result;
    }

    public static async Task SequentialBindZip()
    {
        // Sequence two resource-coupled loads on the railway (the shared-DbContext fix):
        // BindZipAsync awaits the first, runs the second only on success (short-circuits),
        // and zips both into a tuple; BindAsync then continues.
        var result = await LoadNumberAsync()
            .BindZipAsync(_ => LoadNameAsync())
            .BindAsync((number, name) => Result.Ok($"{number}:{name}"));

        _ = result;
    }

    private static Task<Result<int>> LoadNumberAsync() =>
        Task.FromResult(Result.Ok(1));

    private static Task<Result<string>> LoadNameAsync() =>
        Task.FromResult(Result.Ok("ready"));

    private static Task<Result<bool>> LoadAvailableAsync() =>
        Task.FromResult(Result.Ok(true));
}