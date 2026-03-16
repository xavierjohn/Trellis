namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Combine errors into one.
/// If both the errors types are <see cref="ValidationError"/>, the validation errors will be merged.
/// Otherwise, the errors will be wrapped into an <see cref="AggregateError"/>.
/// </summary>
[DebuggerStepThrough]
public static class CombineErrorExtensions
{
    /// <summary>
    /// Combine two errors into one.
    /// If both the errors types are <see cref="ValidationError"/>, the errors will be merged.
    /// Otherwise, the errors will be wrapped in an <see cref="AggregateError"/>.
    /// </summary>
    /// <param name="thisError"></param>
    /// <param name="otherError"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static Error Combine(this Error? thisError, Error otherError)
    {
        ArgumentNullException.ThrowIfNull(otherError);
        if (thisError is null) return otherError;
        if (thisError is ValidationError thisValidation && otherError is ValidationError otherValidation)
            return thisValidation.Merge(otherValidation);

        List<Error> errors = [];
        AddError(thisError);
        AddError(otherError);

        return new AggregateError(errors);

        void AddError(Error error)
        {
            if (error is AggregateError aggregate)
                errors.AddRange(aggregate.Errors);
            else
                errors.Add(error);
        }
    }
}