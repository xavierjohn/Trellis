namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Error type creation, combination, equality, and type checking
/// on the V6 closed-ADT Error type.
/// </summary>
[MemoryDiagnoser]
public class ErrorBenchmarks
{
    private Error _validationError = default!;
    private Error _notFoundError = default!;
    private Error.UnprocessableContent _complexValidationError = default!;

    [GlobalSetup]
    public void Setup()
    {
        _validationError = new Error.UnprocessableContent(
            EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field1"), "validation.error") { Detail = "Test validation error" }));
        _notFoundError = new Error.NotFound(new ResourceRef("Resource", "resource123")) { Detail = "Resource not found" };
        _complexValidationError = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"),    "validation.error") { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "Password is required" },
            new FieldViolation(InputPointer.ForProperty("age"),      "validation.error") { Detail = "Age must be 18 or older" }));
    }

    [Benchmark(Baseline = true)]
    public Error CreateValidationError_Simple()
    {
        return new Error.UnprocessableContent(
            EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("fieldName"), "validation.error") { Detail = "Field is required" }));
    }

    [Benchmark]
    public Error CreateNotFoundError()
    {
        return new Error.NotFound(new ResourceRef("Resource", "id123")) { Detail = "Resource not found" };
    }

    [Benchmark]
    public Error CreateUnauthorizedError()
    {
        return new Error.Unauthorized() { Detail = "Access denied" };
    }

    [Benchmark]
    public Error CreateConflictError()
    {
        return new Error.Conflict(null, "duplicate-id") { Detail = "Resource already exists" };
    }

    [Benchmark]
    public Error CreateInternalServerError()
    {
        return new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Internal server error" };
    }

    [Benchmark]
    public Error.UnprocessableContent CreateValidationError_MultipleFields()
    {
        return new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("field1"), "validation.error") { Detail = "Error 1" },
            new FieldViolation(InputPointer.ForProperty("field2"), "validation.error") { Detail = "Error 2" },
            new FieldViolation(InputPointer.ForProperty("field3"), "validation.error") { Detail = "Error 3" }));
    }

    [Benchmark]
    public Error.UnprocessableContent CreateValidationError_SingleFieldMultipleMessages()
    {
        return new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "Too short" },
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "No special characters" },
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "No numbers" }));
    }

    [Benchmark]
    public bool ErrorEquality_SameKind()
    {
        var error1 = new Error.NotFound(new ResourceRef("Resource", "1")) { Detail = "Resource 1 not found" };
        var error2 = new Error.NotFound(new ResourceRef("Resource", "1")) { Detail = "Resource 1 not found" };
        return error1.Equals(error2);
    }

    [Benchmark]
    public bool ErrorEquality_DifferentKind()
    {
        return _validationError.Equals(_notFoundError);
    }

    [Benchmark]
    public int GetHashCode_ValidationError()
    {
        return _validationError.GetHashCode();
    }

    [Benchmark]
    public int GetHashCode_ComplexValidationError()
    {
        return _complexValidationError.GetHashCode();
    }

    [Benchmark]
    public string ToString_SimpleError()
    {
        return _validationError.ToString()!;
    }

    [Benchmark]
    public string ToString_ComplexValidationError()
    {
        return _complexValidationError.ToString()!;
    }

    [Benchmark]
    public Error CreateErrorFromException()
    {
        var exception = new InvalidOperationException("Test exception");
        return new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = exception.Message };
    }

    [Benchmark]
    public bool IsErrorType_UnprocessableContent()
    {
        return _validationError is Error.UnprocessableContent;
    }

    [Benchmark]
    public bool IsErrorType_NotFound()
    {
        return _notFoundError is Error.NotFound;
    }
}
