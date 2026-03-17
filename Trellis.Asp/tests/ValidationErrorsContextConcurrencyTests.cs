namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Xunit;

/// <summary>
/// Thread-safety and concurrency tests for ValidationErrorsContext.
/// </summary>
public class ValidationErrorsContextConcurrencyTests
{
    [Fact]
    public async Task ConcurrentErrorAddition_AllErrorsCollected()
    {
        // Arrange
        const int threadCount = 50;
        const int errorsPerThread = 10;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add errors from multiple threads concurrently
            var tasks = Enumerable.Range(0, threadCount)
                .Select(threadId => Task.Run(() =>
                {
                    for (int i = 0; i < errorsPerThread; i++)
                    {
                        ValidationErrorsContext.AddError(
                            $"Field{threadId}",
                            $"Error {i} from thread {threadId}");
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();

            // Should have threadCount fields (one per thread)
            error!.FieldErrors.Should().HaveCount(threadCount);

            // Each field should have exactly errorsPerThread errors
            foreach (var fieldError in error.FieldErrors)
            {
                fieldError.Details.Should().HaveCount(errorsPerThread);
            }
        }
    }

    [Fact]
    public async Task ConcurrentDuplicateErrors_NoDuplicatesStored()
    {
        // Arrange
        const int threadCount = 20;
        const string fieldName = "TestField";
        const string errorMessage = "Same error message";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add same error from multiple threads
            var tasks = Enumerable.Range(0, threadCount)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        ValidationErrorsContext.AddError(fieldName, errorMessage);
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().ContainSingle();
            error.FieldErrors[0].FieldName.Should().Be(fieldName);
            // Should only have one copy of the error message (no duplicates)
            error.FieldErrors[0].Details.Should().ContainSingle()
                .Which.Should().Be(errorMessage);
        }
    }

    [Fact]
    public async Task ConcurrentValidationErrorAddition_AllErrorsCollected()
    {
        // Arrange
        const int taskCount = 30;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add ValidationError objects from multiple tasks
            var tasks = Enumerable.Range(0, taskCount)
                .Select(taskId => Task.Run(() =>
                {
                    var validationError = Error.Validation($"Error from task {taskId}", $"Field{taskId}");
                    ValidationErrorsContext.AddError((ValidationError)validationError);
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(taskCount);
        }
    }

    [Fact]
    public async Task ConcurrentGetAndAdd_NoDeadlock()
    {
        // Arrange
        const int iterations = 100;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Concurrently add errors and read them
            var addTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    ValidationErrorsContext.AddError($"Field{i}", $"Error {i}");
                }
            }, TestContext.Current.CancellationToken);

            var readTasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var error = ValidationErrorsContext.GetValidationError();
                        var hasErrors = ValidationErrorsContext.HasErrors;
                        // Just reading, verify no exception
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            // Assert - Should complete without deadlock
            await Task.WhenAll(addTask);
            await Task.WhenAll(readTasks);

            var finalError = ValidationErrorsContext.GetValidationError();
            finalError.Should().NotBeNull();
            finalError!.FieldErrors.Should().HaveCount(iterations);
        }
    }

    [Fact]
    public async Task ConcurrentHasErrors_ConsistentReads()
    {
        // Arrange
        using (ValidationErrorsContext.BeginScope())
        {
            var readResults = new System.Collections.Concurrent.ConcurrentBag<bool>();

            // Act - Read HasErrors from multiple threads while adding errors
            var addTask = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    ValidationErrorsContext.AddError($"Field{i}", $"Error {i}");
                    await Task.Delay(1);
                }
            }, TestContext.Current.CancellationToken);

            var readTasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(async () =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        readResults.Add(ValidationErrorsContext.HasErrors);
                        await Task.Delay(1);
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(addTask);
            await Task.WhenAll(readTasks);

            // Assert - At least some reads should see errors
            readResults.Should().Contain(true, "errors were added during reads");

            // Final state should have errors
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MultipleAsyncScopes_Isolated()
    {
        // Arrange & Act - Create multiple scopes concurrently
        var tasks = Enumerable.Range(0, 20)
            .Select(async scopeId =>
            {
                using (ValidationErrorsContext.BeginScope())
                {
                    // Add unique error for this scope
                    ValidationErrorsContext.AddError($"Scope{scopeId}", $"Error from scope {scopeId}");

                    // Small delay to increase chance of concurrent execution
                    await Task.Delay(5);

                    // Verify this scope only has its error
                    var error = ValidationErrorsContext.GetValidationError();
                    error.Should().NotBeNull();
                    error!.FieldErrors.Should().ContainSingle("scope should be isolated");
                    error.FieldErrors[0].FieldName.Should().Be($"Scope{scopeId}");

                    return true;
                }
            })
            .ToArray();

        // Assert - All should complete successfully
        var results = await Task.WhenAll(tasks);
        results.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task ConcurrentPropertyNameChanges_ThreadSafe()
    {
        // Arrange
        const int taskCount = 50;
        var propertyNames = new System.Collections.Concurrent.ConcurrentBag<string?>();

        // Act - Set property name from multiple threads
        var tasks = Enumerable.Range(0, taskCount)
            .Select(taskId => Task.Run(() =>
            {
                var propertyName = $"Property{taskId}";
                ValidationErrorsContext.CurrentPropertyName = propertyName;

                // Read immediately
                var readValue = ValidationErrorsContext.CurrentPropertyName;
                propertyNames.Add(readValue);

                // Clear it
                ValidationErrorsContext.CurrentPropertyName = null;
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Should complete without exception
        // Note: Due to AsyncLocal isolation, each task sees its own value
        propertyNames.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ConcurrentAddError_WithValidationError_ThreadSafe()
    {
        // Arrange
        const int taskCount = 25;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add complex ValidationError objects concurrently
            var tasks = Enumerable.Range(0, taskCount)
                .Select(taskId => Task.Run(() =>
                {
                    // Create a ValidationError with multiple field errors
                    var error1 = Error.Validation($"Error 1 from task {taskId}", $"Field1_{taskId}");
                    var error2 = Error.Validation($"Error 2 from task {taskId}", $"Field2_{taskId}");

                    ValidationErrorsContext.AddError((ValidationError)error1);
                    ValidationErrorsContext.AddError((ValidationError)error2);
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            // Should have 2 fields per task
            error!.FieldErrors.Should().HaveCount(taskCount * 2);
        }
    }

    [Fact]
    public async Task StressTest_ManyFieldsManyErrors()
    {
        // Arrange
        const int fieldCount = 100;
        const int errorsPerField = 50;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add many errors to many fields concurrently
            var tasks = Enumerable.Range(0, fieldCount)
                .Select(fieldId => Task.Run(() =>
                {
                    for (int errorId = 0; errorId < errorsPerField; errorId++)
                    {
                        ValidationErrorsContext.AddError(
                            $"Field{fieldId}",
                            $"Unique error {errorId}");
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(fieldCount);

            // Each field should have all its errors
            foreach (var fieldError in error.FieldErrors)
            {
                fieldError.Details.Should().HaveCount(errorsPerField);
            }
        }
    }

    [Fact]
    public void NestedScopes_ThreadSafe()
    {
        // Arrange & Act
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Outer", "Outer error");

            // Create nested scope
            using (ValidationErrorsContext.BeginScope())
            {
                ValidationErrorsContext.AddError("Inner", "Inner error");

                // Inner scope should only have inner error
                var innerError = ValidationErrorsContext.GetValidationError();
                innerError!.FieldErrors.Should().ContainSingle()
                    .Which.FieldName.Should().Be("Inner");
            }

            // After inner scope disposed, outer scope should have outer error
            var outerError = ValidationErrorsContext.GetValidationError();
            outerError!.FieldErrors.Should().ContainSingle()
                .Which.FieldName.Should().Be("Outer");
        }
    }

    [Fact]
    public void NestedScopes_RestoreCurrentPropertyName()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "OuterProperty";

            using (ValidationErrorsContext.BeginScope())
            {
                ValidationErrorsContext.CurrentPropertyName = "InnerProperty";
                ValidationErrorsContext.CurrentPropertyName.Should().Be("InnerProperty");
            }

            ValidationErrorsContext.CurrentPropertyName.Should().Be("OuterProperty");
        }

        ValidationErrorsContext.CurrentPropertyName.Should().BeNull();
    }

    [Fact]
    public async Task RapidScopeCreationAndDisposal_NoLeaks()
    {
        // Arrange & Act - Create and dispose many scopes rapidly
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (ValidationErrorsContext.BeginScope())
                    {
                        ValidationErrorsContext.AddError("Test", "Test error");
                    }
                    // Scope should be disposed
                }
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - No scope should be active
        ValidationErrorsContext.Current.Should().BeNull();
    }
}