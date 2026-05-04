namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Asp.Validation;

/// <summary>
/// Cross-cutting regression guard for PR #454: every Trellis.Asp wire-emission point
/// that builds a <c>ValidationProblem</c> dictionary key from a <see cref="FieldViolation"/>
/// must use <see cref="JsonPointerToMvc.Translate(string)"/> so clients see a single
/// consistent error-key shape regardless of which validation path produced the response.
///
/// <para>
/// Before this PR several emitters used <c>Path.TrimStart('/')</c> directly, which left
/// JSON-deserialization and scalar-binding failures returning slash-form keys
/// (<c>items/0/name</c>) while the main ROP path emitted MVC dot+bracket keys
/// (<c>items[0].name</c>). These tests pin every emitter to the centralized translator.
/// </para>
/// <para>
/// <see cref="ScalarValueValidationFilter"/>'s private <c>HandleJsonValidationErrors</c>
/// path uses the same <c>ModelStateDictionary.AddModelError</c> pattern as
/// <see cref="ModelStateExtensions.AddResultErrors"/>; the regression guard for that path
/// is the <c>ModelStateExtensions</c> test below plus the centralized
/// <see cref="JsonPointerToMvcTests"/> contract tests.
/// </para>
/// </summary>
public sealed class MvcWireShapeConsistencyTests
{
    private const string NestedPointer = "/items/0/name";
    private const string MvcKey = "items[0].name";
    private const string SlashKey = "items/0/name";
    private const string ErrorMessage = "must be valid";

    private static Error.UnprocessableContent NestedFieldError() =>
        new(EquatableArray.Create(
            new FieldViolation(new InputPointer(NestedPointer), "format") { Detail = ErrorMessage }));

    [Fact]
    public void ModelStateExtensions_AddResultErrors_uses_MVC_dot_bracket_key()
    {
        var modelState = new ModelStateDictionary();

        modelState.AddResultErrors("body", NestedFieldError());

        modelState.ContainsKey(MvcKey).Should().BeTrue(
            $"expected MVC convention key '{MvcKey}', got: {string.Join(", ", modelState.Keys)}");
        modelState.ContainsKey(SlashKey).Should().BeFalse(
            "JSON Pointer slash form must not appear in ModelState keys");
    }

    [Fact]
    public async Task ScalarValueValidationEndpointFilter_uses_MVC_dot_bracket_key()
    {
        var filter = new ScalarValueValidationEndpointFilter();
        var context = new TestEndpointFilterContext(new DefaultHttpContext());
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError(NestedFieldError());

            var result = await filter.InvokeAsync(context, next);

            result.Should().BeOfType<ProblemHttpResult>();
            var problem = (ProblemHttpResult)result!;
            var validationProblem = problem.ProblemDetails as Microsoft.AspNetCore.Http.HttpValidationProblemDetails;
            validationProblem.Should().NotBeNull("filter must return a validation problem with errors dict");
            validationProblem!.Errors.Should().ContainKey(MvcKey);
            validationProblem.Errors.Should().NotContainKey(SlashKey,
                "JSON Pointer slash form must not appear in ValidationProblem errors keys");
        }
    }

    [Fact]
    public void ScalarValueTypeHelper_GetValidationErrors_uses_MVC_dot_bracket_key()
    {
        // Triggers ExtractErrors via a value object whose TryCreate returns a multi-segment
        // FieldViolation. Confirms the centralised translator runs in the GroupBy/aggregation path.
        var errors = ScalarValueTypeHelper.GetValidationErrors(typeof(NestedFieldVO), "any", "ignored");

        errors.Should().NotBeNull();
        errors!.Should().ContainKey(MvcKey);
        errors.Should().NotContainKey(SlashKey);
    }

    private sealed class TestEndpointFilterContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterContext(HttpContext httpContext) => HttpContext = httpContext;

        public override HttpContext HttpContext { get; }

        public override IList<object?> Arguments => new List<object?>();

        public override T GetArgument<T>(int index) => default!;
    }

    // Test fixture: a value-object-shaped type whose TryCreate(string?, string?) returns
    // a multi-segment FieldViolation, simulating a custom value object that maps a structured
    // input to a nested pointer (e.g. a composite that extracts "/items/0/name" from a payload).
    public readonly struct NestedFieldVO
    {
        public string Value { get; }

        private NestedFieldVO(string value) => Value = value;

        public static Result<NestedFieldVO> TryCreate(string? value, string? fieldName) =>
            Result.Fail<NestedFieldVO>(new Error.UnprocessableContent(
                EquatableArray.Create(
                    new FieldViolation(new InputPointer(NestedPointer), "format") { Detail = ErrorMessage })));
    }
}

