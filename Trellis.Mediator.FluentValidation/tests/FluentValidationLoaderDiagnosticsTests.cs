namespace Trellis.Mediator.FluentValidation.Tests;

using System.Reflection;
using global::FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.Mediator.FluentValidation;

public sealed class FluentValidationLoaderDiagnosticsTests
{
    [Fact]
    public void AddTrellisFluentValidation_WhenReflectionTypeLoadExceptionOccurs_LogsWarning()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        var assembly = new ThrowingAssembly();

        services.AddTrellisFluentValidation(assembly);

        var warning = loggerFactory.Logs.Should().ContainSingle().Which;
        warning.Level.Should().Be(LogLevel.Warning);
        // Log category is intentionally preserved across the v3 package split
        // (Trellis.FluentValidation → Trellis.Mediator.FluentValidation) so that
        // consumer log filters keyed on "Trellis.FluentValidation" continue to fire.
        warning.Category.Should().Be("Trellis.FluentValidation");
        warning.Message.Should().Contain(ThrowingAssembly.AssemblyName);
        warning.Message.Should().Contain(MissingDependencyMessage);
        services.Should().Contain(d => d.ServiceType == typeof(IValidator<DiagnosticCommand>)
            && d.ImplementationType == typeof(LoadableValidator));
    }

    private const string MissingDependencyMessage = "Missing dependency for diagnostic validator.";

    private sealed class ThrowingAssembly : Assembly
    {
        public const string AssemblyName = "Trellis.FluentValidation.DiagnosticsTests";

        public override string FullName => AssemblyName;

        public override Type[] GetTypes()
        {
            Type?[] loadableTypes = [typeof(LoadableValidator), null];
            Exception?[] loaderExceptions = [new TypeLoadException(MissingDependencyMessage)];

            throw new ReflectionTypeLoadException(loadableTypes, loaderExceptions);
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<CapturedLog> _logs = [];

        public IReadOnlyList<CapturedLog> Logs => _logs;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _logs);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string categoryName, List<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => logs.Add(new CapturedLog(logLevel, categoryName, formatter(state, exception)));
    }

    private sealed record CapturedLog(LogLevel Level, string Category, string Message);

    private sealed record DiagnosticCommand : ICommand<Result<string>>;

    private sealed class LoadableValidator : AbstractValidator<DiagnosticCommand>;
}
