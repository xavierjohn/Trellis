namespace Trellis;

/// <summary>
/// Internal sentinel type representing the absence of a meaningful value.
/// Framework code uses this to provide generic implementations for non-generic <see cref="Result"/> operations.
/// Consumer code should use the non-generic <see cref="Result"/> type directly.
/// </summary>
/// <seealso cref="Result"/>
/// <seealso cref="Result{TValue}"/>
internal record struct Unit
{
}