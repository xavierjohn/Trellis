namespace Trellis.Testing;

using Trellis.Authorization;

/// <summary>
/// In-memory fake implementation of <see cref="SharedResourceLoaderById{TResource, TId}"/>
/// that delegates to a <see cref="FakeRepository{TAggregate, TId}"/>.
/// Use in application-layer tests to wire resource-based authorization
/// without hand-writing a loader per command.
/// </summary>
/// <typeparam name="TResource">The aggregate type.</typeparam>
/// <typeparam name="TId">The aggregate ID type.</typeparam>
/// <example>
/// <code>
/// var repo = new FakeRepository&lt;Order, OrderId&gt;();
/// var loader = new FakeSharedResourceLoader&lt;Order, OrderId&gt;(repo);
///
/// // Register in test DI:
/// services.AddScoped&lt;FakeRepository&lt;Order, OrderId&gt;&gt;();
/// services.AddScoped&lt;SharedResourceLoaderById&lt;Order, OrderId&gt;, FakeSharedResourceLoader&lt;Order, OrderId&gt;&gt;();
/// </code>
/// </example>
public class FakeSharedResourceLoader<TResource, TId> : SharedResourceLoaderById<TResource, TId>
    where TResource : Aggregate<TId>
    where TId : notnull
{
    private readonly FakeRepository<TResource, TId> _repository;

    /// <summary>
    /// Creates a new fake shared resource loader backed by the specified repository.
    /// </summary>
    /// <param name="repository">The fake repository to delegate lookups to.</param>
    public FakeSharedResourceLoader(FakeRepository<TResource, TId> repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public override Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)
        => _repository.GetByIdAsync(id, cancellationToken);
}