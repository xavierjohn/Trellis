namespace Trellis;

using System.Diagnostics.CodeAnalysis;

public static partial class MaybeExtensions
{
    /// <summary>
    /// Converts the <see cref="Nullable{T}"/> struct to a <see cref="Maybe{T}"/>.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="value">The nullable value to convert.</param>
    /// <returns>Returns the <see cref="Maybe{T}"/> equivalent to the <see cref="Nullable{T}"/>.</returns>
    public static Maybe<T> AsMaybe<T>(this T? value) where T : struct =>
        value is null ? default : new(value.Value);

    /// <summary>
    /// Wraps the class instance in a <see cref="Maybe{T}"/>.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="value">The potentially null class instance.</param>
    /// <returns>Returns <see cref="Maybe{T}.None"/> if the class instance is null, otherwise returns a Maybe with the value.</returns>
    public static Maybe<T> AsMaybe<T>([MaybeNull] this T value) where T : class =>
        value is null ? default : new(value);
}