namespace Trellis.EntityFrameworkCore;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// EF Core options extension that marks a <see cref="Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"/>
/// as having Trellis interceptors registered, and records the <see cref="System.TimeProvider"/>
/// the registration was made with so repeat calls with a conflicting <c>TimeProvider</c> can
/// fail fast rather than silently dropping the consumer's choice.
/// </summary>
/// <remarks>
/// The extension has no runtime behavior beyond presence + TimeProvider identity tracking.
/// Repeat calls to <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)"/>
/// (or any of its overloads) skip re-registration when the requested <c>TimeProvider</c>
/// matches the recorded one. A repeat call that supplies a DIFFERENT <c>TimeProvider</c>
/// throws <see cref="System.InvalidOperationException"/> — the prior behavior was to silently
/// no-op, which let a library's parameterless registration shadow an application's later
/// custom-clock registration without diagnostic.
/// </remarks>
internal sealed class TrellisInterceptorsMarkerExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// The <see cref="System.TimeProvider"/> the first <c>AddTrellisInterceptors</c> call
    /// recorded. <c>null</c> when the first call used the parameterless overload (which
    /// implies <see cref="System.TimeProvider.System"/>).
    /// </summary>
    public System.TimeProvider? RecordedTimeProvider { get; }

    public TrellisInterceptorsMarkerExtension(System.TimeProvider? recordedTimeProvider = null) =>
        RecordedTimeProvider = recordedTimeProvider;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) { }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(TrellisInterceptorsMarkerExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "TrellisInterceptors ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Trellis:Interceptors"] = "1";
    }
}