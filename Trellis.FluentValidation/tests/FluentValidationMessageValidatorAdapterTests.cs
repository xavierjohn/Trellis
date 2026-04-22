namespace Trellis.FluentValidation.Tests;

using global::FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.FluentValidation;
using Trellis.Mediator;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="FluentValidationMessageValidatorAdapter{TMessage}"/> and
/// <see cref="FluentValidationServiceCollectionExtensions"/>.
/// </summary>
public class FluentValidationMessageValidatorAdapterTests
{
    #region Adapter — pass-through and aggregation

    [Fact]
    public async Task ValidateAsync_no_validators_returns_success()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>([]);
        var command = new CreateUserCommand("Alice", "alice@example.com");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_all_validators_pass_returns_success()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>(
            [new CreateUserCommandNameValidator(), new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand("Alice", "alice@example.com");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_single_validator_failure_returns_unprocessable_content()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>(
            [new CreateUserCommandNameValidator()]);
        var command = new CreateUserCommand(string.Empty, "alice@example.com");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/Name");
    }

    [Fact]
    public async Task ValidateAsync_multiple_validators_aggregates_failures()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>(
            [new CreateUserCommandNameValidator(), new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand(string.Empty, "not-an-email");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(2);
        error.Fields.Items.Should().Contain(fv => fv.Field.Path == "/Name");
        error.Fields.Items.Should().Contain(fv => fv.Field.Path == "/Email");
    }

    [Fact]
    public async Task ValidateAsync_uses_validator_error_code_when_provided()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>(
            [new CreateUserCommandEmailValidator()]);
        var command = new CreateUserCommand("Alice", "bad");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("email.invalid");
    }

    [Fact]
    public async Task ValidateAsync_normalizes_nested_property_paths_to_json_pointer()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateOrderCommand>(
            [new CreateOrderCommandValidator()]);
        var command = new CreateOrderCommand(new OrderAddress(string.Empty), [new OrderLine(string.Empty)]);

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        var paths = error.Fields.Items.Select(fv => fv.Field.Path).ToArray();
        paths.Should().Contain("/Address/Zip");
        paths.Should().Contain("/Lines/0/Sku");
    }

    [Fact]
    public async Task ValidateAsync_root_failure_uses_message_type_name_as_field()
    {
        var adapter = new FluentValidationMessageValidatorAdapter<CreateUserCommand>(
            [new CreateUserCommandRootValidator()]);
        var command = new CreateUserCommand("Alice", "alice@example.com");

        var result = await adapter.ValidateAsync(command, CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/CreateUserCommand");
    }

    #endregion

    #region Registration — AddTrellisFluentValidation()

    [Fact]
    public void AddTrellisFluentValidation_registers_open_generic_message_validator()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IMessageValidator<>)
            && d.ImplementationType == typeof(FluentValidationMessageValidatorAdapter<>));
    }

    [Fact]
    public void AddTrellisFluentValidation_called_twice_registers_adapter_only_once()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation();
        services.AddTrellisFluentValidation();

        services
            .Where(d => d.ServiceType == typeof(IMessageValidator<>)
                && d.ImplementationType == typeof(FluentValidationMessageValidatorAdapter<>))
            .Should().ContainSingle("registration must be idempotent so the adapter resolves once per scope");
    }

    [Fact]
    public async Task AddTrellisFluentValidation_called_twice_each_validator_runs_only_once()
    {
        var services = new ServiceCollection();
        services.AddTrellisFluentValidation();
        services.AddTrellisFluentValidation();
        services.AddScoped<IValidator<CreateUserCommand>, CreateUserCommandNameValidator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // Mirror the production path: ValidationBehavior injects IEnumerable<IMessageValidator<T>>
        // and runs every resolved instance. Using GetRequiredService here would mask duplicate
        // adapter registrations because it returns only the last-registered descriptor.
        var adapters = scope.ServiceProvider
            .GetServices<IMessageValidator<CreateUserCommand>>()
            .ToList();

        adapters.Should().ContainSingle("a duplicate adapter registration would cause IValidator<T> to run twice per request");

        var result = await adapters[0].ValidateAsync(
            new CreateUserCommand(string.Empty, "ada@example.com"),
            CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCount(1);
    }

    [Fact]
    public void AddTrellisFluentValidation_does_not_register_a_pipeline_behavior()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation();

        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void AddTrellisFluentValidation_with_assemblies_scans_and_registers_validators()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IValidator<CreateUserCommand>)
            && d.ImplementationType == typeof(CreateUserCommandNameValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IValidator<CreateUserCommand>)
            && d.ImplementationType == typeof(CreateUserCommandEmailValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IMessageValidator<>)
            && d.ImplementationType == typeof(FluentValidationMessageValidatorAdapter<>));
    }

    [Fact]
    public void AddTrellisFluentValidation_with_empty_assemblies_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisFluentValidation([]);

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    [Fact]
    public void AddTrellisFluentValidation_with_null_assembly_element_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisFluentValidation([null!]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("assemblies")
            .WithMessage("*index 0*");
    }

    [Fact]
    public void AddTrellisFluentValidation_called_twice_with_same_assembly_does_not_double_register_validators()
    {
        var services = new ServiceCollection();

        services.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        services.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);

        services
            .Where(d => d.ServiceType == typeof(IValidator<CreateUserCommand>)
                && d.ImplementationType == typeof(CreateUserCommandNameValidator))
            .Should().ContainSingle("validators must not be registered twice when the same assembly is scanned twice");
        services
            .Where(d => d.ServiceType == typeof(IValidator<CreateUserCommand>)
                && d.ImplementationType == typeof(CreateUserCommandEmailValidator))
            .Should().ContainSingle();
    }

    [Fact]
    public void AddTrellisFluentValidation_with_duplicate_assemblies_in_one_call_does_not_double_register()
    {
        var services = new ServiceCollection();
        var assembly = typeof(FluentValidationMessageValidatorAdapterTests).Assembly;

        services.AddTrellisFluentValidation(assembly, assembly);

        services
            .Where(d => d.ServiceType == typeof(IValidator<CreateUserCommand>)
                && d.ImplementationType == typeof(CreateUserCommandNameValidator))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task AddTrellisFluentValidation_called_twice_each_validator_runs_only_once_per_message()
    {
        var command = new CreateUserCommand(string.Empty, "bad");

        // Baseline: scan once.
        var singleServices = new ServiceCollection();
        singleServices.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        using var singleProvider = singleServices.BuildServiceProvider();
        using var singleScope = singleProvider.CreateScope();
        var singleAdapter = singleScope.ServiceProvider.GetRequiredService<IMessageValidator<CreateUserCommand>>();
        var singleResult = await singleAdapter.ValidateAsync(command, CancellationToken.None);
        var singleError = ExtractError(singleResult).Should().BeOfType<Error.UnprocessableContent>().Which;
        var baselineCount = singleError.Fields.Items.Length;

        // Scan twice — must produce the same count, not double.
        var doubleServices = new ServiceCollection();
        doubleServices.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        doubleServices.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        using var doubleProvider = doubleServices.BuildServiceProvider();
        using var doubleScope = doubleProvider.CreateScope();
        var doubleAdapter = doubleScope.ServiceProvider.GetRequiredService<IMessageValidator<CreateUserCommand>>();
        var doubleResult = await doubleAdapter.ValidateAsync(command, CancellationToken.None);
        var doubleError = ExtractError(doubleResult).Should().BeOfType<Error.UnprocessableContent>().Which;

        doubleError.Fields.Items.Length.Should().Be(baselineCount, "scanning the same assembly twice must not double validator execution");
    }

    [Fact]
    public void AddTrellisFluentValidation_resolves_validators_through_di()
    {
        var services = new ServiceCollection();
        services.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var validators = scope.ServiceProvider.GetServices<IValidator<CreateUserCommand>>().ToList();

        validators.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task End_to_end_adapter_resolved_via_DI_aggregates_scanned_validator_failures()
    {
        var services = new ServiceCollection();
        services.AddTrellisFluentValidation(typeof(FluentValidationMessageValidatorAdapterTests).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IMessageValidator<CreateUserCommand>>();

        var result = await adapter.ValidateAsync(
            new CreateUserCommand(string.Empty, "bad"),
            CancellationToken.None);

        var error = ExtractError(result).Should().BeOfType<Error.UnprocessableContent>().Which;
        error.Fields.Items.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Helpers

    private static Error ExtractError(IResult result)
    {
        result.TryGetError(out var error).Should().BeTrue();
        return error!;
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

    internal sealed class CreateUserCommandRootValidator : AbstractValidator<CreateUserCommand>
    {
        public CreateUserCommandRootValidator()
            => RuleFor(x => x).Custom((_, ctx) => ctx.AddFailure(string.Empty, "Root level rule failed."));
    }

    internal sealed record OrderAddress(string Zip);
    internal sealed record OrderLine(string Sku);

    internal sealed record CreateOrderCommand(OrderAddress Address, IReadOnlyList<OrderLine> Lines)
        : ICommand<Result<string>>;

    internal sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
    {
        public CreateOrderCommandValidator()
        {
            RuleFor(x => x.Address.Zip).NotEmpty();
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.Sku).NotEmpty());
        }
    }

    #endregion
}
