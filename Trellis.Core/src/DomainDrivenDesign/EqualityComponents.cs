namespace Trellis;

using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Collects the components that define a <see cref="ValueObject"/>'s structural equality.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="ref struct" /> sink passed by reference to
/// <see cref="ValueObject.GetEqualityComponents(ref EqualityComponents)"/>. Components are written
/// into a caller-owned inline buffer, so equality, ordering, and hashing traverse a value object's
/// components without iterator allocations or boxing supported BCL scalars. Custom comparable
/// value types can still box. The previous <c>IEnumerable&lt;IComparable?&gt;</c> contract allocated
/// an iterator state machine on every comparison.
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
    private Span<EqualityComponent> buffer;
    private EqualityComponent[]? rented;
    private int count;

    internal EqualityComponents(Span<EqualityComponent> initialBuffer)
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
    public void Add(IComparable? component) => Append(EqualityComponent.FromReference(component));

    /// <summary>
    /// Adds a typed component without boxing supported BCL scalar values.
    /// Custom comparable values retain their object-based equality behavior.
    /// </summary>
    /// <typeparam name="T">The component's comparable type.</typeparam>
    /// <param name="component">The component value. A null reference sorts before non-null values.</param>
    public void Add<T>(T component) where T : IComparable? => Append(EqualityComponent.From(component));

    /// <summary>
    /// Adds a nullable value-type component, treating absence as a null component.
    /// Supported BCL scalar values are added without boxing.
    /// </summary>
    /// <typeparam name="T">The underlying comparable value type.</typeparam>
    /// <param name="component">The nullable component value.</param>
    public void Add<T>(T? component) where T : struct, IComparable
    {
        if (component.HasValue)
            Add(component.GetValueOrDefault());
        else
            Append(default);
    }

    /// <summary>
    /// Adds an optional component, treating <see cref="Maybe{T}.None"/> as a <see langword="null"/>
    /// component so present and absent values order consistently. Supported BCL scalar values
    /// are added without boxing.
    /// </summary>
    /// <typeparam name="T">The optional value's type.</typeparam>
    /// <param name="maybe">The optional value.</param>
    [OverloadResolutionPriority(1)]
    public void Add<T>(Maybe<T> maybe)
        where T : notnull, IComparable
    {
        if (maybe.TryGetValue(out var component))
            Add(component);
        else
            Append(default);
    }

    internal readonly ReadOnlySpan<EqualityComponent> AsSpan() => buffer[..count];

    internal void Return()
    {
        if (rented is null)
            return;

        // Components contain references the pool would otherwise keep alive until the next rent.
        ArrayPool<EqualityComponent>.Shared.Return(rented, clearArray: true);
        rented = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Append(EqualityComponent component)
    {
        if (count == buffer.Length)
            Grow();

        buffer[count++] = component;
    }

    private void Grow()
    {
        var replacement = ArrayPool<EqualityComponent>.Shared.Rent(buffer.Length * 2);
        buffer[..count].CopyTo(replacement);

        var previous = rented;
        rented = replacement;
        buffer = replacement;

        if (previous is not null)
            ArrayPool<EqualityComponent>.Shared.Return(previous, clearArray: true);
    }
}