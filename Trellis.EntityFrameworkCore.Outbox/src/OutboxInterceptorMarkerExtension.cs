namespace Trellis.EntityFrameworkCore;

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// EF Core options extension that marks a <see cref="Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"/>
/// as already carrying the outbox capture interceptor, so repeated
/// <c>AddTrellisOutboxInterceptor</c> calls are idempotent. Without this guard a second
/// registration of the same interceptor instance would make EF invoke it twice per
/// <c>SaveChanges</c>, capturing every domain event into two <see cref="OutboxMessage"/> rows.
/// </summary>
/// <remarks>
/// The extension has no runtime behavior beyond presence tracking, mirroring
/// <see cref="TrellisInterceptorsMarkerExtension"/>.
/// </remarks>
internal sealed class OutboxInterceptorMarkerExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) { }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(OutboxInterceptorMarkerExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "TrellisOutboxInterceptor ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Trellis:OutboxInterceptor"] = "1";
    }
}
