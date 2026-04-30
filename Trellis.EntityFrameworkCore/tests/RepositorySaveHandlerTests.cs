using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public sealed partial class RepositorySaveHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrderSaveDbContext _context;
    private readonly OrderRepository _repository;
    private readonly CreateOrderHandler _handler;
    private readonly TransactionalCommandBehavior<CreateOrderCommand, Result<OrderId>> _behavior;

    public RepositorySaveHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OrderSaveDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new OrderSaveDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new OrderRepository(_context);
        _handler = new CreateOrderHandler(_repository);
        _behavior = new TransactionalCommandBehavior<CreateOrderCommand, Result<OrderId>>(new EfUnitOfWork<OrderSaveDbContext>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_Success_StagesAggregateAndPipelinePersistsIt()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = OrderId.Create(Guid.NewGuid());
        var command = new CreateOrderCommand(id, OrderNumber.Create("ORD-001"));

        // Act
        var result = await _behavior.Handle(command, _handler.Handle, ct);

        // Assert
        result.Should().BeSuccess();
        result.TryGetValue(out var persistedId).Should().BeTrue();
        persistedId.Should().Be(id);

        _context.ChangeTracker.Clear();
        var persisted = await _context.Orders.FindAsync([id], ct);
        persisted.Should().NotBeNull();
        persisted!.Number.Should().Be(OrderNumber.Create("ORD-001"));
    }

    [Fact]
    public async Task Handle_DuplicateKeyCommitFailure_ReturnsConflictFailure()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = OrderId.Create(Guid.NewGuid());
        var existing = Order.Create(id, OrderNumber.Create("ORD-001"));
        existing.TryGetValue(out var order).Should().BeTrue();
        _context.Orders.Add(order!);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var command = new CreateOrderCommand(id, OrderNumber.Create("ORD-002"));

        // Act
        var result = await _behavior.Handle(command, _handler.Handle, ct);

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
    }

    private sealed partial class OrderId : RequiredGuid<OrderId>;

    [StringLength(32)]
    private sealed partial class OrderNumber : RequiredString<OrderNumber>;

    private sealed class Order : Aggregate<OrderId>
    {
        private Order(OrderId id, OrderNumber number) : base(id) => Number = number;

        private Order() : base(default!) => Number = default!;

        public OrderNumber Number { get; private set; }

        public static Result<Order> Create(OrderId id, OrderNumber number) =>
            Result.Ok(new Order(id, number));
    }

    private sealed class OrderSaveDbContext(DbContextOptions<OrderSaveDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(OrderId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(builder =>
            {
                builder.HasKey(order => order.Id);
                builder.Property(order => order.Number).HasMaxLength(32).IsRequired();
            });
    }

    private sealed class OrderRepository(DbContext context) : RepositoryBase<Order, OrderId>(context)
    {
        public Result<OrderId> StageNew(OrderId id, OrderNumber number) =>
            Order.Create(id, number)
                .Tap(Add)
                .Map(order => order.Id);
    }

    private sealed record CreateOrderCommand(OrderId Id, OrderNumber Number) : ICommand<Result<OrderId>>;

    private sealed class CreateOrderHandler(OrderRepository repository) : ICommandHandler<CreateOrderCommand, Result<OrderId>>
    {
        public ValueTask<Result<OrderId>> Handle(CreateOrderCommand command, CancellationToken cancellationToken) =>
            repository.StageNew(command.Id, command.Number).AsValueTask();
    }
}