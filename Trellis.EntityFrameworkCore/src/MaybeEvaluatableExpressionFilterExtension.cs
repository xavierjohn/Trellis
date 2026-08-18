namespace Trellis.EntityFrameworkCore;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// EF Core options extension that registers <see cref="MaybeEvaluatableExpressionFilterPlugin"/>
/// in the per-context internal service provider.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEvaluatableExpressionFilterPlugin"/> is an EF Core internal service registered via
/// <c>TryAddEnumerable</c> with singleton lifetime. Per-<see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// registration requires an <see cref="IDbContextOptionsExtension"/> whose
/// <see cref="ApplyServices"/> method adds the plugin to the internal services collection.
/// </para>
/// <para>
/// Applied via
/// <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors{TContext}(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder{TContext})"/>;
/// repeated calls are idempotent because <c>AddOrUpdateExtension</c> replaces by extension type.
/// </para>
/// </remarks>
internal sealed class MaybeEvaluatableExpressionFilterExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEvaluatableExpressionFilterPlugin, MaybeEvaluatableExpressionFilterPlugin>());

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(MaybeEvaluatableExpressionFilterExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "MaybeEvalFilter ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Trellis:MaybeEvaluatableExpressionFilter"] = "1";
    }
}