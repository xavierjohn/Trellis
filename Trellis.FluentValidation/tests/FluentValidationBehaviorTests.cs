namespace Trellis.FluentValidation.Tests;

using global::FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.FluentValidation;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="FluentValidationBehavior{TMessage, TResponse}"/> and
/// <see cref="FluentValidationServiceCollectionExtensions"/>.
/// </summary>
public class FluentValidationBehaviorTests
{
    #region Behavior — pass-through

    [Fact]
    public async Task Handle_no_validators_calls_next_and_returns_handler_result()
    {
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>([]);
        var command = new CreateUserCommand("Alice", "alice@example.com");
        var nextInvoked = false;
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
        {
            nextInvoked = true;
            return new ValueTask<Result<string>>(Result.Ok("ok"));
        }

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        nextInvoked.Should().BeTrue();
        result.Unwrap().Should().Be("ok");
    }

    [Fact]
    public async Task Handle_all_validators_pass_calls_next()
    {
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>(
            [new CreateUserCommandNameValidator(), new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand("Alice", "alice@example.com");
        var nextInvoked = false;
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
        {
            nextInvoked = true;
            return new ValueTask<Result<string>>(Result.Ok("ok"));
        }

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        nextInvoked.Should().BeTrue();
        result.Unwrap().Should().Be("ok");
    }

    #endregion

    #region Behavior — short-circuit on failure

    [Fact]
    public async Task Handle_single_validator_failure_short_circuits_with_unprocessable_content()
    {
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>(
            [new CreateUserCommandNameValidator()]);
        var command = new CreateUserCommand(string.Empty, "alice@example.com");
        var nextInvoked = false;
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
        {
            nextInvoked = true;
            return new ValueTask<Result<string>>(Result.Ok("should not reach"));
        }

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        nextInvoked.Should().BeFalse();
        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Contain("Name");
    }

    [Fact]
    public async Task Handle_multiple_validators_aggregates_failures_into_one_error()
    {
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>(
            [new CreateUserCommandNameValidator(), new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand(string.Empty, "not-an-email");
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
            => new(Result.Ok("should not reach"));

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(2);
        error.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Name"));
        error.Fields.Items.Should().Contain(fv => fv.Field.Path.Contains("Email"));
    }

    [Fact]
    public async Task Handle_uses_validator_error_code_when_provided()
    {
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>(
            [new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand("Alice", "bad");
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
            => new(Result.Ok("nope"));

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("email.invalid");
    }

    #endregion

    #region Registration — AddTrellisFluentValidation()

    [Fact]
    public void AddTrellisFluentValidation_registers_open_generic_behavior()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(FluentValidationBehavior<,>));
    }

    [Fact]
    public void AddTrellisFluentValidation_with_assemblies_scans_and_registers_validators()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation(typeof(FluentValidationBehaviorTests).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IValidator<CreateUserCommand>)
            && d.ImplementationType == typeof(CreateUserCommandNameValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IValidator<CreateUserCommand>)
            && d.ImplementationType == typeof(CreateUserCommandEmailValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(FluentValidationBehavior<,>));
    }

    [Fact]
    public void AddTrellisFluentValidation_with_empty_assemblies_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisFluentValidation([]);

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    [Fact]
    public void AddTrellisFluentValidation_resolves_validators_through_di()
    {
        var services = new ServiceCollection();
        services.AddTrellisFluentValidation(typeof(FluentValidationBehaviorTests).Assembly);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var validators = scope.ServiceProvider.GetServices<IValidator<CreateUserCommand>>().ToList();

        validators.Should().HaveCount(2);
    }

    #endregion

    #region End-to-end — behavior resolved + executed via DI

    [Fact]
    public async Task Behavior_resolved_via_DI_aggregates_failures_from_scanned_validators()
    {
        var services = new ServiceCollection();
        services.AddTrellisFluentValidation(typeof(FluentValidationBehaviorTests).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var validators = scope.ServiceProvider.GetServices<IValidator<CreateUserCommand>>();
        var behavior = new FluentValidationBehavior<CreateUserCommand, Result<string>>(validators);
        var command = new CreateUserCommand(string.Empty, "bad");
        ValueTask<Result<string>> Next(CreateUserCommand _, CancellationToken __)
            => new(Result.Ok("nope"));

        var result = await behavior.Handle(command, Next, CancellationToken.None);

        var error = result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(2);
    }

    #endregion

    #region Test fixtures

    internal sealed record CreateUserCommand(string Name, string Email)
        : ICommand<Result<string>>;

    internal sealed class CreateUserCommandNameValidator : AbstractValidator<CreateUserCommand>
    {
        public CreateUserCommandNameValidator()
            => RuleFor(x => x.Name).NotEmpty().WithMessage("Name required.");
    }

    internal sealed class CreateUserCommandEmailValidator : AbstractValidator<CreateUserCommand>
    {
        public CreateUserCommandEmailValidator()
            => RuleFor(x => x.Email).EmailAddress()
                .WithErrorCode("email.invalid")
                .WithMessage("Email must be valid.");
    }

    #endregion
}
