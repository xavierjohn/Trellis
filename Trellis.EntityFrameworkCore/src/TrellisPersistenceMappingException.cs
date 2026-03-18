namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Represents a failure to materialize a Trellis value object from persisted data.
/// </summary>
/// <remarks>
/// This exception is thrown when EF Core reads a database value and the corresponding
/// Trellis value object cannot be reconstructed through its validation factory.
/// </remarks>
public sealed class TrellisPersistenceMappingException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisPersistenceMappingException"/> class.
    /// </summary>
    public TrellisPersistenceMappingException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisPersistenceMappingException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public TrellisPersistenceMappingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisPersistenceMappingException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TrellisPersistenceMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisPersistenceMappingException"/> class.
    /// </summary>
    /// <param name="valueObjectType">The Trellis value object type that could not be materialized.</param>
    /// <param name="persistedValue">The persisted value that failed materialization.</param>
    /// <param name="factoryMethod">The factory method used during materialization.</param>
    /// <param name="detail">The validation or mapping detail describing the failure.</param>
    /// <param name="innerException">The underlying exception, if one was thrown.</param>
    public TrellisPersistenceMappingException(
        Type valueObjectType,
        object? persistedValue,
        string factoryMethod,
        string detail,
        Exception? innerException = null)
        : base(BuildMessage(valueObjectType, persistedValue, factoryMethod, detail), innerException)
    {
        ValueObjectType = valueObjectType;
        PersistedValue = persistedValue;
        FactoryMethod = factoryMethod;
        Detail = detail;
    }

    /// <summary>
    /// Gets the Trellis value object type that could not be materialized.
    /// </summary>
    public Type ValueObjectType { get; } = typeof(object);

    /// <summary>
    /// Gets the persisted value that failed materialization.
    /// </summary>
    public object? PersistedValue { get; }

    /// <summary>
    /// Gets the factory method used during materialization.
    /// </summary>
    public string FactoryMethod { get; } = string.Empty;

    /// <summary>
    /// Gets the validation or mapping detail describing the failure.
    /// </summary>
    public string Detail { get; } = string.Empty;

    private static string BuildMessage(Type valueObjectType, object? persistedValue, string factoryMethod, string detail)
    {
        var formattedValue = persistedValue is null ? "<null>" : $"'{persistedValue}'";
        return $"Failed to materialize Trellis value object '{valueObjectType.Name}' from persisted value {formattedValue} using {factoryMethod}. {detail}";
    }
}