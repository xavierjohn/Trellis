namespace Trellis;

using System.Runtime.CompilerServices;

internal readonly struct EqualityComponent : IEquatable<EqualityComponent>
{
    private readonly object? _component;
    // Int128 provides readonly, reference-free 16-byte storage; the comparer retains the scalar's type.
    private readonly Int128 _scalar;

    private EqualityComponent(object? component)
    {
        _component = component;
        _scalar = default;
    }

    private EqualityComponent(ScalarComparer comparer, Int128 scalar)
    {
        _component = comparer;
        _scalar = scalar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static EqualityComponent From<T>(T component) where T : IComparable?
    {
        if (typeof(T) == typeof(string))
            return new EqualityComponent(component);

        if (!typeof(T).IsValueType)
            return typeof(T).IsInterface ? FromReference(component) : new EqualityComponent(component);

        if (typeof(T) == typeof(int)) return FromScalar(Unsafe.As<T, int>(ref component));
        if (typeof(T) == typeof(long)) return FromScalar(Unsafe.As<T, long>(ref component));
        if (typeof(T) == typeof(decimal)) return FromScalar(Unsafe.As<T, decimal>(ref component));
        if (typeof(T) == typeof(Guid)) return FromScalar(Unsafe.As<T, Guid>(ref component));
        if (typeof(T) == typeof(bool)) return FromScalar(Unsafe.As<T, bool>(ref component));
        if (typeof(T) == typeof(DateTime)) return FromScalar(Unsafe.As<T, DateTime>(ref component));
        if (typeof(T) == typeof(DateTimeOffset)) return FromScalar(Unsafe.As<T, DateTimeOffset>(ref component));
        if (typeof(T) == typeof(byte)) return FromScalar(Unsafe.As<T, byte>(ref component));
        if (typeof(T) == typeof(sbyte)) return FromScalar(Unsafe.As<T, sbyte>(ref component));
        if (typeof(T) == typeof(short)) return FromScalar(Unsafe.As<T, short>(ref component));
        if (typeof(T) == typeof(ushort)) return FromScalar(Unsafe.As<T, ushort>(ref component));
        if (typeof(T) == typeof(uint)) return FromScalar(Unsafe.As<T, uint>(ref component));
        if (typeof(T) == typeof(ulong)) return FromScalar(Unsafe.As<T, ulong>(ref component));
        if (typeof(T) == typeof(char)) return FromScalar(Unsafe.As<T, char>(ref component));
        if (typeof(T) == typeof(float)) return FromScalar(Unsafe.As<T, float>(ref component));
        if (typeof(T) == typeof(double)) return FromScalar(Unsafe.As<T, double>(ref component));
        if (typeof(T) == typeof(Half)) return FromScalar(Unsafe.As<T, Half>(ref component));
        if (typeof(T) == typeof(Int128)) return FromScalar(Unsafe.As<T, Int128>(ref component));
        if (typeof(T) == typeof(UInt128)) return FromScalar(Unsafe.As<T, UInt128>(ref component));
        if (typeof(T) == typeof(nint)) return FromScalar(Unsafe.As<T, nint>(ref component));
        if (typeof(T) == typeof(nuint)) return FromScalar(Unsafe.As<T, nuint>(ref component));
        if (typeof(T) == typeof(DateOnly)) return FromScalar(Unsafe.As<T, DateOnly>(ref component));
        if (typeof(T) == typeof(TimeOnly)) return FromScalar(Unsafe.As<T, TimeOnly>(ref component));
        if (typeof(T) == typeof(TimeSpan)) return FromScalar(Unsafe.As<T, TimeSpan>(ref component));

        // Custom structs retain object equality, which may differ from their IEquatable<T> contract.
        return new EqualityComponent(component);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static EqualityComponent FromReference(IComparable? component) => new(component);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(EqualityComponent other)
    {
        if (_component is null)
            return other._component is null;
        if (other._component is null)
            return false;

        if (_component is string text)
            return text.Equals(other._component);

        if (_component is ScalarComparer comparer)
            return other._component is ScalarComparer
                ? ReferenceEquals(comparer, other._component) && comparer.AreEqual(in _scalar, in other._scalar)
                : comparer.AreEqual(in _scalar, other._component);

        if (other._component is ScalarComparer otherComparer)
            return _component.GetType() == otherComparer.Type
                ? otherComparer.AreEqual(in other._scalar, _component)
                : _component.Equals(otherComparer.Box(in other._scalar));

        return _component.Equals(other._component);
    }

    public override bool Equals(object? obj) => obj is EqualityComponent other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _component is ScalarComparer comparer
        ? comparer.Hash(in _scalar)
        : _component?.GetHashCode() ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CompareTo(EqualityComponent other)
    {
        if (_component is string text && other._component is string otherText)
            return string.CompareOrdinal(text, otherText);

        if (_component is null)
            return other._component is null ? 0 : -1;
        if (other._component is null)
            return 1;

        if (_component is ScalarComparer comparer && ReferenceEquals(comparer, other._component))
            return comparer.Compare(in _scalar, in other._scalar);

        return CompareOther(_component, other._component, in _scalar, in other._scalar);
    }

    private static int CompareOther(object left, object right, in Int128 leftScalar, in Int128 rightScalar)
    {
        var thisType = left is ScalarComparer leftComparer ? leftComparer.Type : left.GetType();
        var otherType = right is ScalarComparer rightComparer ? rightComparer.Type : right.GetType();
        if (thisType != otherType)
            return string.CompareOrdinal(thisType.AssemblyQualifiedName, otherType.AssemblyQualifiedName);

        if (left is ScalarComparer scalarLeft)
            return scalarLeft.CompareToBoxed(in leftScalar, right);
        if (right is ScalarComparer scalarRight)
            return scalarRight.CompareBoxedTo(left, in rightScalar);

        if (left is string text)
            return string.CompareOrdinal(text, (string)right);

        return ((IComparable)left).CompareTo(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static EqualityComponent FromScalar<T>(T component)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        if (Unsafe.SizeOf<T>() > Unsafe.SizeOf<Int128>())
            throw new InvalidOperationException("The scalar exceeds equality component storage.");

        Int128 storage = default;
        Unsafe.As<Int128, T>(ref storage) = component;
        return new EqualityComponent(ScalarComparer<T>.Instance, storage);
    }

    private abstract class ScalarComparer(Type type)
    {
        internal Type Type { get; } = type;
        internal abstract bool AreEqual(in Int128 left, in Int128 right);
        internal abstract bool AreEqual(in Int128 scalar, object boxed);
        internal abstract int Compare(in Int128 left, in Int128 right);
        internal abstract int CompareToBoxed(in Int128 scalar, object boxed);
        internal abstract int CompareBoxedTo(object boxed, in Int128 scalar);
        internal abstract int Hash(in Int128 storage);
        internal abstract object Box(in Int128 storage);
    }

    private sealed class ScalarComparer<T> : ScalarComparer
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        internal static readonly ScalarComparer<T> Instance = new();

        private ScalarComparer() : base(typeof(T)) { }

        internal override bool AreEqual(in Int128 left, in Int128 right) =>
            EqualityComparer<T>.Default.Equals(Read(in left), Read(in right));

        internal override bool AreEqual(in Int128 scalar, object boxed) =>
            boxed is T value && EqualityComparer<T>.Default.Equals(Read(in scalar), value);

        internal override int Compare(in Int128 left, in Int128 right) =>
            Comparer<T>.Default.Compare(Read(in left), Read(in right));

        internal override int CompareToBoxed(in Int128 scalar, object boxed) =>
            Comparer<T>.Default.Compare(Read(in scalar), (T)boxed);

        internal override int CompareBoxedTo(object boxed, in Int128 scalar) =>
            Comparer<T>.Default.Compare((T)boxed, Read(in scalar));

        internal override int Hash(in Int128 storage) =>
            EqualityComparer<T>.Default.GetHashCode(Read(in storage));

        internal override object Box(in Int128 storage) => Read(in storage);

        private static T Read(in Int128 storage) =>
            Unsafe.As<Int128, T>(ref Unsafe.AsRef(in storage));
    }
}