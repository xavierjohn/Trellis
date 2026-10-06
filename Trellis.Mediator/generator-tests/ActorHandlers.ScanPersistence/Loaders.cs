namespace ActorHandlers.ScanPersistence;

using ActorHandlers.ScanApplication;
using Trellis;
using Trellis.Authorization;

public sealed class ScanResourceLoader : SharedResourceLoaderById<ScanResource, string>
{
    private readonly ScanProbe _probe;

    public ScanResourceLoader(ScanProbe probe)
    {
        _probe = probe;
        probe.LoaderConstructions++;
    }

    public override Task<Result<ScanResource>> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        _probe.LeafLoads++;
        _probe.Operations.Add("load");
        var resource = new ScanResource(id, "owner", _probe);
        _probe.Loaded = resource;
        return Task.FromResult(Result.Ok(resource));
    }
}

public sealed class ScanDocumentLoader : SharedResourceLoaderById<ScanDocument, string>
{
    private readonly ScanProbe _probe;

    public ScanDocumentLoader(ScanProbe probe)
    {
        _probe = probe;
        probe.LoaderConstructions++;
    }

    public override Task<Result<ScanDocument>> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        _probe.LeafLoads++;
        _probe.Operations.Add("load");
        var resource = new ScanDocument(id, "folder", _probe);
        _probe.Loaded = resource;
        return Task.FromResult(Result.Ok(resource));
    }
}

public sealed class ScanOwnerLoader(ScanProbe probe) : SharedResourceLoaderById<ScanOwner, string>
{
    public override Task<Result<ScanOwner>> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        probe.Operations.Add("load-owner");
        return Task.FromResult(Result.Ok(new ScanOwner(id, "owner")));
    }
}
