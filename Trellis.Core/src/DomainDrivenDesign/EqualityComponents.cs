namespace Trellis;

using System.Buffers;

/// <summary>
/// Collects the components that define a <see cref="ValueObject"/>'s structural equality.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="ref struct" /> sink passed by reference to
/// <see cref="ValueObject.GetEqualityComponents(ref EqualityComponents)"/>. Components are written
/// into a caller-owned inline buffer, so equality, ordering, and hashing traverse a value object's
/// components without allocating. The previous <c>IEnumerable&lt;IComparable?&gt;</c> contract
/// allocated an iterator state machine on every comparison.
/// </para>
/// <para>
/// Add components in a stable, deterministic order. Derived types that extend a base value object
/// should call <c>base.GetEqualityComponents(ref components)</c> first so the base components keep
/// their positions.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// protected override void GetEqualityComponents(ref EqualityComponents components)
/// {
///     components.Add(Street);
///     components.Add(City);
///     components.Add(PostalCode);
/// }
/// </code>
/// </example>
public ref struct EqualityComponents
{
    private Span<IComparable?> buffer;
    private IComparable?[]? rented;
    private int count;

    internal EqualityComponents(Span<IComparable?> initialBuffer)
    {
        buffer = initialBuffer;
        rented = null;
        count = 0;
    }

    /// <summary>The number of components added so far.</summary>
    public readonly int Count => count;

    /// <summary>
    /// Adds a component to the equality definition. <see langword="null"/> is a valid component and
    /// sorts before every non-null component.
    /// </summary>
    /// <param name="component">The component value.</param>
    public void Add(IComparable? component)
    {
        if (count == buffer.Length)
            Grow();

        buffer[count++] = component;
    }

    /// <summary>
    /// Adds an optional component, treating <see cref="Maybe{T}.None"/> as a <see langword="null"/>
    /// component so present and absent values order consistently.
    /// </summary>
    /// <typeparam name="T">The optional value's type.</typeparam>
    /// <param name="maybe">The optional value.</param>
    public void Add<T>(Maybe<T> maybe)
        where T : notnull, IComparable
        => Add(maybe.HasValue ? maybe.Value : null);

    internal readonly ReadOnlySpan<IComparable?> AsSpan() => buffer[..count];

    internal void Return()
    {
        if (rented is null)
            return;

        // Components are references the pool would otherwise keep alive until the next rent.
        ArrayPool<IComparable?>.Shared.Return(rented, clearArray: true);
        rented = null;
    }

    private void Grow()
    {
        var replacement = ArrayPool<IComparable?>.Shared.Rent(buffer.Length * 2);
        buffer[..count].CopyTo(replacement);

        var previous = rented;
        rented = replacement;
        buffer = replacement;

        if (previous is not null)
            ArrayPool<IComparable?>.Shared.Return(previous, clearArray: true);
    }
}