using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

namespace AuthorizationExample;

// ── Commands ────────────────────────────────────────────────────────────────────
// Each command declares its own authorization and validation requirements.
// The pipeline behaviors enforce them before the handler runs.

/// <summary>
/// Any authenticated user can create a document. Validates the title.
/// </summary>
public sealed record CreateDocumentCommand(string Title, string Content)
    : ICommand<Result<Document>>, IValidate
{
    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Title)
            ? Result.Fail<Document>(Error.Validation("Title is required", "Title"))
            : Result.Ok();
}

/// <summary>
/// Only the document owner (or users with Documents.EditAny) can edit.
/// Authorization is declared via <see cref="IAuthorizeResource{TResource}"/> — the pipeline
/// loads the document via <see cref="EditDocumentResourceLoader"/>, then calls
/// <see cref="Authorize"/> with the loaded resource.
/// </summary>
public sealed record EditDocumentCommand(string DocumentId, string NewContent)
    : ICommand<Result<Document>>, IAuthorizeResource<Document>, IValidate
{
    public IResult Authorize(Actor actor, Document document) =>
        actor.Id == document.OwnerId || actor.HasPermission("Documents.EditAny")
            ? Result.Ok()
            : Result.Fail(Error.Forbidden("Only the owner can edit this document"));

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(NewContent)
            ? Result.Fail<Document>(Error.Validation("Content is required", "Content"))
            : Result.Ok();
}

/// <summary>
/// Loads the document required by <see cref="EditDocumentCommand"/> for authorization.
/// </summary>
public sealed class EditDocumentResourceLoader(DocumentStore store)
    : IResourceLoader<EditDocumentCommand, Document>
{
    public Task<Result<Document>> LoadAsync(
        EditDocumentCommand message, CancellationToken cancellationToken)
    {
        var doc = store.Get(message.DocumentId);
        return doc is not null
            ? Task.FromResult(Result.Ok(doc))
            : Task.FromResult(Result.Fail<Document>(Error.NotFound("Document not found")));
    }
}

/// <summary>
/// Publishing requires the Documents.Publish permission.
/// Authorization is declared via <see cref="IAuthorize"/> — the pipeline checks
/// the actor's permissions automatically.
/// </summary>
public sealed record PublishDocumentCommand(string DocumentId)
    : ICommand<Result<Document>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Publish"];
}

// ── Handlers ────────────────────────────────────────────────────────────────────
// Handlers contain ONLY business logic — no authorization, no validation.
// Compare with DocumentService in DirectServiceExample.cs where auth is mixed in.

public sealed class CreateDocumentHandler(DocumentStore store, IActorProvider actorProvider)
    : ICommandHandler<CreateDocumentCommand, Result<Document>>
{
    public async ValueTask<Result<Document>> Handle(
        CreateDocumentCommand command, CancellationToken cancellationToken)
    {
        var actor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var doc = new Document(Guid.NewGuid().ToString(), actor.Id, command.Title, command.Content);
        store.Add(doc);
        return Result.Ok(doc);
    }
}

public sealed class EditDocumentHandler(DocumentStore store)
    : ICommandHandler<EditDocumentCommand, Result<Document>>
{
    public ValueTask<Result<Document>> Handle(
        EditDocumentCommand command, CancellationToken cancellationToken)
    {
        var doc = store.Get(command.DocumentId);
        if (doc is null)
            return new ValueTask<Result<Document>>(
                Result.Fail<Document>(Error.NotFound("Document not found")));

        var updated = doc with { Content = command.NewContent };
        store.Update(updated);
        return new ValueTask<Result<Document>>(Result.Ok(updated));
    }
}

public sealed class PublishDocumentHandler(DocumentStore store)
    : ICommandHandler<PublishDocumentCommand, Result<Document>>
{
    public ValueTask<Result<Document>> Handle(
        PublishDocumentCommand command, CancellationToken cancellationToken)
    {
        var doc = store.Get(command.DocumentId);
        if (doc is null)
            return new ValueTask<Result<Document>>(
                Result.Fail<Document>(Error.NotFound("Document not found")));

        var published = doc with { IsPublished = true };
        store.Update(published);
        return new ValueTask<Result<Document>>(Result.Ok(published));
    }
}

// ── Example Runner ──────────────────────────────────────────────────────────────

/// <summary>
/// Part 2 — With CQRS.
/// Authorization is declared on commands and enforced by pipeline behaviors.
/// Handlers contain only business logic.
/// </summary>
public static class MediatorExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine(" Part 2: Mediator Pipeline (CQRS)");
        Console.WriteLine(" Authorization is declared on commands.");
        Console.WriteLine(" Pipeline behaviors enforce it automatically.");
        Console.WriteLine(" Handlers contain only business logic.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var actorProvider = new InMemoryActorProvider(Actors.Alice);
        var store = new DocumentStore();

        var services = new ServiceCollection();
        services.AddMediator();
        services.AddTrellisBehaviors();
        services.AddResourceAuthorization(typeof(EditDocumentCommand).Assembly);
        services.AddSingleton<IActorProvider>(actorProvider);
        services.AddSingleton(store);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // 1. Alice creates a document
        actorProvider.CurrentActor = Actors.Alice;
        var createResult = await mediator.Send(new CreateDocumentCommand("Design Doc", "Initial draft"));
        Print("Alice creates 'Design Doc'", createResult);
        if (!createResult.TryGetValue(out var createdDoc))
            throw new InvalidOperationException("Expected create to succeed.");
        var docId = createdDoc.Id;

        // 2. Alice edits her own document (owner)
        actorProvider.CurrentActor = Actors.Alice;
        Print("Alice edits her document",
            await mediator.Send(new EditDocumentCommand(docId, "Updated by Alice")));

        // 3. Bob tries to edit Alice's document (not owner, no EditAny)
        actorProvider.CurrentActor = Actors.Bob;
        Print("Bob tries to edit Alice's document",
            await mediator.Send(new EditDocumentCommand(docId, "Updated by Bob")));

        // 4. Charlie edits Alice's document (has Documents.EditAny)
        actorProvider.CurrentActor = Actors.Charlie;
        Print("Charlie edits Alice's document",
            await mediator.Send(new EditDocumentCommand(docId, "Updated by Charlie")));

        // 5. Bob tries to publish (no Documents.Publish permission)
        actorProvider.CurrentActor = Actors.Bob;
        Print("Bob tries to publish",
            await mediator.Send(new PublishDocumentCommand(docId)));

        // 6. Alice publishes her document (has Documents.Publish)
        actorProvider.CurrentActor = Actors.Alice;
        Print("Alice publishes her document",
            await mediator.Send(new PublishDocumentCommand(docId)));
    }

    private static void Print(string action, Result<Document> result) =>
        Console.WriteLine(result.Match(
            doc => $"  {action,-45} → ✅ {(doc.IsPublished ? "Published" : "Success")}",
            error => $"  {action,-45} → ❌ {error.Detail}"));
}