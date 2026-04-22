using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ValidationBehavior{TMessage, TResponse}"/>.
/// </summary>
public class ValidationBehaviorTests
{
    #region Valid message — handler is called

    [Fact]
    public async Task Handle_ValidMessage_CallsNextAndReturnsHandlerResult()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([]);
        var command = new TestCommand("Alice");
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("Hello, Alice!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Hello, Alice!");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Invalid message — handler is NOT called

    [Fact]
    public async Task Handle_InvalidMessage_DoesNotCallNextAndReturnsFailure()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([]);
        var command = new TestCommand("   ");
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var validation = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Name"));
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked for invalid messages");
    }

    [Fact]
    public async Task Handle_NullName_ReturnsValidationFailure()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([]);
        var command = new TestCommand(null!);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Validation with query

    [Fact]
    public async Task Handle_ValidQuery_CallsNextAndReturnsHandlerResult()
    {
        var behavior = new ValidationBehavior<TestQuery, Result<string>>([]);
        var query = new TestQuery(42);
        var (next, tracker) = NextDelegate.TrackingAsync<TestQuery, Result<string>>(
            Result.Ok("Result-42"));

        var result = await behavior.Handle(query, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Result-42");
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidQuery_ReturnsValidationFailure()
    {
        var behavior = new ValidationBehavior<TestQuery, Result<string>>([]);
        var query = new TestQuery(-1);
        var (next, tracker) = NextDelegate.TrackingAsync<TestQuery, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(query, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var validation = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Id"));
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region IMessageValidator integration — external validators

    [Fact]
    public async Task Handle_external_validator_failure_short_circuits_with_aggregated_error()
    {
        var external = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(UpcWith("Name", "external rule")));
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommandNoValidation("anything"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.Detail.Should().Be("external rule");
    }

    [Fact]
    public async Task Handle_external_validator_success_calls_next()
    {
        var external = new StubMessageValidator<TestCommandNoValidation>(Result.Ok());
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("ok"));

        var result = await behavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeTrue();
        result.Unwrap().Should().Be("ok");
    }

    [Fact]
    public async Task Handle_no_validate_and_no_external_validators_passes_through()
    {
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("ok"));

        var result = await behavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeTrue();
        result.Unwrap().Should().Be("ok");
    }

    [Fact]
    public async Task Handle_aggregates_IValidate_failure_with_external_validator_failure()
    {
        var external = new StubMessageValidator<TestCommand>(
            Result.Fail(UpcWith("Email", "email invalid")));
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommand("   "), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(2);
        error.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Name"));
        error.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Email"));
    }

    [Fact]
    public async Task Handle_aggregates_failures_across_multiple_external_validators()
    {
        var first = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(UpcWith("A", "rule a")));
        var second = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(UpcWith("B", "rule b")));
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([first, second]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(2);
        error.Fields.Items.Should().Contain(fv => fv.Field.Path == "/A");
        error.Fields.Items.Should().Contain(fv => fv.Field.Path == "/B");
    }

    [Fact]
    public async Task Handle_external_validator_returning_non_unprocessable_error_short_circuits_immediately()
    {
        var first = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(new Error.Conflict(null, "conflict.detected") { Detail = "concurrency" }));
        var secondInvoked = false;
        var second = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(UpcWith("X", "should not run")),
            onInvoked: () => secondInvoked = true);
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([first, second]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        secondInvoked.Should().BeFalse("non-UPC failure must short-circuit before subsequent validators");
        result.UnwrapError().Should().BeOfType<Error.Conflict>();
    }

    [Fact]
    public async Task Handle_external_validator_returning_non_unprocessable_does_not_aggregate_with_IValidate_failure()
    {
        var external = new StubMessageValidator<TestCommand>(
            Result.Fail(new Error.Forbidden("forbidden") { Detail = "no access" }));
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommand, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommand("   "), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_aggregates_rule_violations_from_IValidate_and_external_validators()
    {
        var ruleA = new RuleViolation("rule.a") { Detail = "rule a failed" };
        var ruleB = new RuleViolation("rule.b") { Detail = "rule b failed" };
        var ivalidateError = new Error.UnprocessableContent(
            EquatableArray<FieldViolation>.Empty,
            EquatableArray.Create(ruleA));
        var externalError = new Error.UnprocessableContent(
            EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("Email"), "validation.error") { Detail = "bad email" }),
            EquatableArray.Create(ruleB));

        var external = new StubMessageValidator<TestCommandNoValidation>(Result.Fail(externalError));
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("nope"));

        var ivalidateExternal = new StubMessageValidator<TestCommandNoValidation>(Result.Fail(ivalidateError));
        var bothBehavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([ivalidateExternal, external]);
        var result = await bothBehavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse();
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(1);
        error.Fields.Items.Should().Contain(fv => fv.Field.Path == "/Email");
        error.Rules.Items.Should().HaveCount(2);
        error.Rules.Items.Select(r => r.ReasonCode).Should().BeEquivalentTo(["rule.a", "rule.b"]);
    }

    [Fact]
    public async Task Handle_rule_only_failure_still_short_circuits_handler()
    {
        var rule = new RuleViolation("rule.only") { Detail = "rule failure with no field" };
        var external = new StubMessageValidator<TestCommandNoValidation>(
            Result.Fail(new Error.UnprocessableContent(
                EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(rule))));
        var behavior = new ValidationBehavior<TestCommandNoValidation, Result<string>>([external]);
        var (next, tracker) = NextDelegate.TrackingAsync<TestCommandNoValidation, Result<string>>(
            Result.Ok("nope"));

        var result = await behavior.Handle(new TestCommandNoValidation("x"), next, CancellationToken.None);

        tracker.WasInvoked.Should().BeFalse("rule-only UPC failures must still short-circuit");
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().BeEmpty();
        error.Rules.Items.Should().HaveCount(1);
        error.Rules.Items[0].ReasonCode.Should().Be("rule.only");
    }

    private static Error.UnprocessableContent UpcWith(string field, string detail)
        => new(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = detail }));

    private sealed class StubMessageValidator<TMessage>(IResult result, Action? onInvoked = null)
        : IMessageValidator<TMessage>
        where TMessage : global::Mediator.IMessage
    {
        public ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken)
        {
            onInvoked?.Invoke();
            return new ValueTask<IResult>(result);
        }
    }

    #endregion
}
