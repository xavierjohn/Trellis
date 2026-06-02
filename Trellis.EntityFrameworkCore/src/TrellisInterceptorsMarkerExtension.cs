namespace Trellis.EntityFrameworkCore;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// EF Core options extension that marks a <see cref="Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"/>
/// as having Trellis interceptors registered.
/// </summary>
/// <remarks>
/// This extension intentionally has no runtime behavior. It exists solely as a presence marker so
/// repeated calls to <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)"/>
/// do not append duplicate Trellis interceptors.
/// </remarks>
internal sealed class TrellisInterceptorsMarkerExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

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