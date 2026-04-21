
namespace Trellis;

public static partial class MaybeExtensions
{
    /// <summary>
    /// Converts the <see cref="Maybe{T}"/> to a <see cref="Nullable"/> struct.
    /// </summary>
    /// <returns>Returns the <see cref="Nullable{T}"/> equivalent to the <see cref="Maybe{T}"/>.</returns>
    public static T? AsNullable<T>(in this Maybe<T> value)
        where T : struct
    {
        if (value.TryGetValue(out var result))
        {
            return result;
        }

        return default;
    }
}