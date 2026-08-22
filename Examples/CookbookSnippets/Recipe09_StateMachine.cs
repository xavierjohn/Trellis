// Cookbook Recipe 9 — State machine: CanFire + Fire pattern with FireResult.
namespace CookbookSnippets.Recipe09;

using Stateless;
using Trellis;
using Trellis.StateMachine;

public partial class DocumentState : RequiredEnum<DocumentState>
{
    public static readonly DocumentState Draft = new();
    public static readonly DocumentState Submitted = new();
    public static readonly DocumentState Approved = new();
}

public partial class DocumentTrigger : RequiredEnum<DocumentTrigger>
{
    public static readonly DocumentTrigger Submit = new();
    public static readonly DocumentTrigger Approve = new();
    public static readonly DocumentTrigger Reject = new();
}

public sealed class Document
{
    public DocumentState State { get; set; } = DocumentState.Draft;
}

public sealed class DocumentService
{
    public static Result<DocumentState> Submit(Document doc)
    {
        var machine = new StateMachine<DocumentState, DocumentTrigger>(doc.State);
        machine.Configure(DocumentState.Draft).Permit(DocumentTrigger.Submit, DocumentState.Submitted);
        machine.Configure(DocumentState.Submitted)
               .Permit(DocumentTrigger.Approve, DocumentState.Approved)
               .Permit(DocumentTrigger.Reject, DocumentState.Draft);

        Result<DocumentState> result = machine.FireResult(DocumentTrigger.Submit);
        return result.Tap(newState => doc.State = newState);
    }
}
internal static class Recipe9StateMachineSurface
{
    public static void LazyStateMachine_FireResultSurface(Document document)
    {
        var machine = new LazyStateMachine<DocumentState, DocumentTrigger>(
            () => document.State,
            state => document.State = state,
            Configure);

        StateMachine<DocumentState, DocumentTrigger> stateless = machine.Machine;
        Result<DocumentState> result = machine.FireResult(DocumentTrigger.Submit);

        _ = (stateless, result);
    }

    public static void InvalidTransition_ErrorSurface()
    {
        var machine = new StateMachine<DocumentState, DocumentTrigger>(DocumentState.Draft);
        machine.Configure(DocumentState.Draft).Permit(DocumentTrigger.Submit, DocumentState.Submitted);

        Result<DocumentState> invalid = machine.FireResult(DocumentTrigger.Approve);
        Error? error = invalid.Error;
        Error.InvariantViolation? invariant = error as Error.InvariantViolation;
        string reasonCode = invariant?.Code ?? string.Empty;

        _ = reasonCode;
    }

    private static void Configure(StateMachine<DocumentState, DocumentTrigger> machine)
    {
        machine.Configure(DocumentState.Draft).Permit(DocumentTrigger.Submit, DocumentState.Submitted);
        machine.Configure(DocumentState.Submitted)
               .Permit(DocumentTrigger.Approve, DocumentState.Approved)
               .Permit(DocumentTrigger.Reject, DocumentState.Draft);
    }
}
