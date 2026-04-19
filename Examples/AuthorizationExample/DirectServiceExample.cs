using Trellis;
using Trellis.Authorization;

namespace AuthorizationExample;

/// <summary>
/// Part 1 — Without CQRS.
/// Authorization is checked manually in each service method.
/// Uses only Trellis.Authorization — no Mediator dependency.
/// </summary>
public static class DirectServiceExample
{
    public static void Run()
    {
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine(" Part 1: Direct Service (No CQRS)");
        Console.WriteLine(" Authorization is checked manually in each service method.");
        Console.WriteLine(" Uses only Trellis.Authorization — no Mediator dependency.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Actors.PrintActors();

        var service = new DocumentService();

        // 1. Alice creates a document
        var createResult = service.CreateDocument(Actors.Alice, "Design Doc", "Initial draft");
        Print("Alice creates 'Design Doc'", createResult);
        if (!createResult.TryGetValue(out var createdDoc))
            throw new InvalidOperationException("Expected create to succeed.");
        var docId = createdDoc.Id;

        // 2. Alice edits her own document (owner)
        Print("Alice edits her document",
            service.EditDocument(Actors.Alice, docId, "Updated by Alice"));

        // 3. Bob tries to edit Alice's document (not owner, no EditAny)
        Print("Bob tries to edit Alice's document",
            service.EditDocument(Actors.Bob, docId, "Updated by Bob"));

        // 4. Charlie edits Alice's document (has Documents.EditAny)
        Print("Charlie edits Alice's document",
            service.EditDocument(Actors.Charlie, docId, "Updated by Charlie"));

        // 5. Bob tries to publish (no Documents.Publish permission)
        Print("Bob tries to publish",
            service.PublishDocument(Actors.Bob, docId));

        // 6. Alice publishes her document (has Documents.Publish)
        Print("Alice publishes her document",
            service.PublishDocument(Actors.Alice, docId));
    }

    private static void Print(string action, Result<Document> result) =>
        Console.WriteLine(result.Match(
            doc => $"  {action,-45} → ✅ {(doc.IsPublished ? "Published" : "Success")}",
            error => $"  {action,-45} → ❌ {error.Detail}"));
}

/// <summary>
/// A service that manually checks authorization in each method.
/// Authorization, validation, and business logic are mixed together.
/// Compare this with the CQRS handlers in MediatorExample.cs —
/// those handlers contain ZERO authorization code.
/// </summary>
public sealed class DocumentService
{
    private readonly DocumentStore _store = new();

    public Result<Document> CreateDocument(Actor actor, string title, string content)
    {
        var doc = new Document(Guid.NewGuid().ToString(), actor.Id, title, content);
        _store.Add(doc);
        return Result.Ok(doc);
    }

    public Result<Document> EditDocument(Actor actor, string documentId, string newContent)
    {
        var doc = _store.Get(documentId);
        if (doc is null)
            return Result.Fail<Document>(Error.NotFound("Document not found"));

        // ⚠️ Auth logic mixed with business logic
        if (actor.Id != doc.OwnerId && !actor.HasPermission("Documents.EditAny"))
            return Result.Fail<Document>(Error.Forbidden("Only the owner can edit this document"));

        var updated = doc with { Content = newContent };
        _store.Update(updated);
        return Result.Ok(updated);
    }

    public Result<Document> PublishDocument(Actor actor, string documentId)
    {
        var doc = _store.Get(documentId);
        if (doc is null)
            return Result.Fail<Document>(Error.NotFound("Document not found"));

        // ⚠️ Auth logic mixed with business logic
        if (!actor.HasPermission("Documents.Publish"))
            return Result.Fail<Document>(Error.Forbidden("Missing required permission: Documents.Publish"));

        var published = doc with { IsPublished = true };
        _store.Update(published);
        return Result.Ok(published);
    }
}