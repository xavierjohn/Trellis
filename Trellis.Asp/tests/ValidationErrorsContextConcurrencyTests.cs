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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();

            // Each thread uses a unique field name with errorsPerThread distinct messages
            error!.Fields.Items.Should().HaveCount(threadCount * errorsPerThread);
            error.Fields.Items.GroupBy(fv => fv.Field.Path).Should().HaveCount(threadCount);
            foreach (var group in error.Fields.Items.GroupBy(fv => fv.Field.Path))
            {
                group.Should().HaveCount(errorsPerThread);
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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields.Items[0].Field.Path.Should().Be("/" + fieldName);
            // Should only have one copy of the error message (no duplicates)
            error.Fields.Items[0].Detail.Should().Be(errorMessage);
        }
    }

    [Fact]
    public async Task ConcurrentValidationErrorAddition_AllErrorsCollected()
    {
        // Arrange
        const int taskCount = 30;

        using (ValidationErrorsContext.BeginScope())
        {
            // Act - Add Error.UnprocessableContent objects from multiple tasks
            var tasks = Enumerable.Range(0, taskCount)
                .Select(taskId => Task.Run(() =>
                {
                    var validationError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty($"Field{taskId}"), "validation.error") { Detail = $"Error from task {taskId}" }));
                    ValidationErrorsContext.AddError((Error.UnprocessableContent)validationError);
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(taskCount);
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
                        var error = ValidationErrorsContext.GetUnprocessableContent();
                        var hasErrors = ValidationErrorsContext.HasErrors;
                        // Just reading, verify no exception
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            // Assert - Should complete without deadlock
            await Task.WhenAll(addTask);
            await Task.WhenAll(readTasks);

            var finalError = ValidationErrorsContext.GetUnprocessableContent();
            finalError.Should().NotBeNull();
            finalError!.Fields.Items.Should().HaveCount(iterations);
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
                    var error = ValidationErrorsContext.GetUnprocessableContent();
                    error.Should().NotBeNull();
                    error!.Fields.Items.Should().ContainSingle("scope should be isolated");
                    error.Fields[0].Field.Path.Should().Be($"/Scope{scopeId}");

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
            // Act - Add complex Error.UnprocessableContent objects concurrently
            var tasks = Enumerable.Range(0, taskCount)
                .Select(taskId => Task.Run(() =>
                {
                    // Create a Error.UnprocessableContent with multiple field errors
                    var error1 = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty($"Field1_{taskId}"), "validation.error") { Detail = $"Error 1 from task {taskId}" }));
                    var error2 = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty($"Field2_{taskId}"), "validation.error") { Detail = $"Error 2 from task {taskId}" }));

                    ValidationErrorsContext.AddError((Error.UnprocessableContent)error1);
                    ValidationErrorsContext.AddError((Error.UnprocessableContent)error2);
                }, TestContext.Current.CancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            // Should have 2 fields per task
            error!.Fields.Items.Should().HaveCount(taskCount * 2);
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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(fieldCount * errorsPerField);

            // Each field should appear errorsPerField times
            error.Fields.Items.GroupBy(fv => fv.Field.Path).Should().HaveCount(fieldCount);
            foreach (var group in error.Fields.Items.GroupBy(fv => fv.Field.Path))
            {
                group.Should().HaveCount(errorsPerField);
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
                var innerError = ValidationErrorsContext.GetUnprocessableContent();
                innerError!.Fields.Items.Should().ContainSingle()
                    .Which.Field.Path.Should().Be("/Inner");
            }

            // After inner scope disposed, outer scope should have outer error
            var outerError = ValidationErrorsContext.GetUnprocessableContent();
            outerError!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Outer");
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