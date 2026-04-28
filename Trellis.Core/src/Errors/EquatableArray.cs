namespace Trellis;

using System.Collections.Immutable;

/// <summary>
/// Wraps an <see cref="ImmutableArray{T}"/> to provide structural (sequence) equality.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImmutableArray{T}"/>'s default equality is reference-based: two distinct
/// arrays with identical contents compare unequal. This wrapper restores sequence
/// equality so collection-bearing records work as expected.
/// </para>
/// <para>
/// A default-constructed <see cref="EquatableArray{T}"/> represents an empty (uninitialized)
/// sequence; two default values compare equal.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>
    /// Initializes a new instance wrapping the provided <see cref="ImmutableArray{T}"/>.
    /// </summary>
    /// <param name="items">The items to wrap.</param>
    public EquatableArray(ImmutableArray<T> items) => _items = items;

    /// <summary>
    /// Gets the wrapped items. Never returns a default-uninitialized array; an empty
    /// array is returned in that case.
    /// </summary>
    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    /// <summary>
    /// Gets the number of items in the sequence.
    /// </summary>
    public int Length => Items.Length;

    /// <summary>
    /// Gets a value indicating whether the sequence is empty.
    /// </summary>
    public bool IsEmpty => Items.IsEmpty;

    /// <summary>
    /// Gets the element at the specified zero-based index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index.</returns>
    public T this[int index] => Items[index];

    /// <summary>
    /// An empty <see cref="EquatableArray{T}"/>.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types — EquatableArray<T>.Empty mirrors ImmutableArray<T>.Empty.
    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    /// <summary>
    /// Creates an <see cref="EquatableArray{T}"/> from the provided items.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <returns>A new <see cref="EquatableArray{T}"/>.</returns>
    public static EquatableArray<T> Create(params T[] items) => new(items.ToImmutableArray());

    /// <summary>
    /// Creates an <see cref="EquatableArray{T}"/> from the provided items.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <returns>A new <see cref="EquatableArray{T}"/>.</returns>
    public static EquatableArray<T> From(IEnumerable<T> items) => new(items.ToImmutableArray());
#pragma warning restore CA1000

    /// <summary>
    /// Returns an enumerator that iterates through the items.
    /// </summary>
    /// <returns>An enumerator for the items.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(EquatableArray<T> other)
    {
        var a = Items;
        var b = other.Items;
        if (a.Length != b.Length) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
            if (!cmp.Equals(a[i], b[i])) return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> ea && Equals(ea);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var item in Items) hc.Add(item);
        return hc.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>
    /// Implicit conversion from <see cref="ImmutableArray{T}"/>.
    /// </summary>
    /// <param name="items">The items.</param>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);
}

/// <summary>
/// Non-generic factory helpers for <see cref="EquatableArray{T}"/> that allow type inference.
/// </summary>
public static class EquatableArray
{
    /// <summary>
    /// Creates an <see cref="EquatableArray{T}"/> from the provided items, inferring <typeparamref name="T"/>.
    /// </summary>
    public static EquatableArray<T> Create<T>(params T[] items) => new(items.ToImmutableArray());

    /// <summary>
    /// Creates an <see cref="EquatableArray{T}"/> from an enumerable of items, inferring <typeparamref name="T"/>.
    /// </summary>
    public static EquatableArray<T> From<T>(IEnumerable<T> items) => new(items.ToImmutableArray());
}