// Cookbook Recipe 30 — Rehydrating entities from persistence: fail-loud vs Result-track.
namespace CookbookSnippets.Recipe30;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public sealed partial class UserId : RequiredGuid<UserId>;

public sealed partial class FirstName : RequiredString<FirstName>;

public sealed partial class LastName : RequiredString<LastName>;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;

public sealed class User : Aggregate<UserId>
{
    private User(UserId id) : base(id) { }

    public FirstName FirstName { get; private init; } = default!;
    public LastName LastName { get; private init; } = default!;
    public EmailAddress Email { get; private init; } = default!;

    public static Result<User> TryCreate(UserId id, FirstName firstName, LastName lastName, EmailAddress email) =>
        Result.Ok(new User(id) { FirstName = firstName, LastName = lastName, Email = email });
}

public sealed class UserRow
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
}

public interface IUserRepository
{
    Task<Result<User>> FindByIdAsync(UserId id, CancellationToken ct);
}

// Pattern A — fail-loud rehydration (the 90% case). Write-path TryCreate is guaranteed, so a
// failure here is corruption or migration drift, not something the application caller can fix.
public sealed class UserRepository(IReadOnlyList<UserRow> rows) : IUserRepository
{
    public Task<Result<User>> FindByIdAsync(UserId id, CancellationToken ct)
    {
        UserRow? row = rows.FirstOrDefault(r => r.Id == id.Value);
        if (row is null)
            return Task.FromResult(Result.Fail<User>(new Error.NotFound(ResourceRef.For<User>(id))));

        User user = User.TryCreate(
            UserId.TryCreate(row.Id).GetValueOrThrow($"Corrupt User.Id in row {row.Id}"),
            FirstName.TryCreate(row.FirstName).GetValueOrThrow($"Corrupt User.FirstName in row {row.Id}"),
            LastName.TryCreate(row.LastName).GetValueOrThrow($"Corrupt User.LastName in row {row.Id}"),
            EmailAddress.TryCreate(row.Email).GetValueOrThrow($"Corrupt User.Email in row {row.Id}"))
            .GetValueOrThrow($"Corrupt User aggregate for row {row.Id}");

        return Task.FromResult(Result.Ok(user));
    }
}

public sealed partial class ContactId : RequiredGuid<ContactId>;

public sealed class Contact : Aggregate<ContactId>
{
    private Contact(ContactId id) : base(id) { }

    public FirstName FirstName { get; private init; } = default!;
    public EmailAddress Email { get; private init; } = default!;

    public static Result<Contact> TryCreate(ContactId id, FirstName firstName, EmailAddress email) =>
        Result.Ok(new Contact(id) { FirstName = firstName, Email = email });
}

public sealed class ContactRow
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
}

public interface ILegacyContactRepository
{
    Task<Result<Contact>> FindByIdAsync(ContactId id, CancellationToken ct);
}

// Pattern B — Result-track end-to-end. Rows were imported from a v1 system that never ran the
// current TryCreate constraints, so per-field failures stay on the Result track.
public sealed class LegacyContactRepository(IReadOnlyList<ContactRow> rows) : ILegacyContactRepository
{
    public Task<Result<Contact>> FindByIdAsync(ContactId id, CancellationToken ct)
    {
        ContactRow? row = rows.FirstOrDefault(r => r.Id == id.Value);
        if (row is null)
            return Task.FromResult(Result.Fail<Contact>(new Error.NotFound(ResourceRef.For<Contact>(id))));

        return Task.FromResult(
            Result.Combine(
                    ContactId.TryCreate(row.Id, "Id"),
                    FirstName.TryCreate(row.FirstName, "FirstName"),
                    EmailAddress.TryCreate(row.Email, "Email"))
                .Bind(Contact.TryCreate));
    }
}

internal static class Recipe30Demonstrator
{
    // The application layer matches on the typed failure to pick a wire shape.
    public static string DescribeOutcome(Result<Contact> result) =>
        result.Match(
            onSuccess: contact => $"200 {contact.Email}",
            onFailure: err => err switch
            {
                Error.NotFound => "404",
                Error.InvalidInput invalid => $"422 ({invalid.Fields.Length} field(s))",
                _ => "500",
            });
}

#if FALSE
// Wrong — Trellis.Testing.Unwrap() in production code mixes test and production seams.
// var user = User.TryCreate(UserId.TryCreate(row.Id).Unwrap(), ...).Unwrap();

// Wrong — inline .Match(v => v, e => throw ...) reinvents the exception shape at every call site.
// var user = User.TryCreate(
//     UserId.TryCreate(row.Id).Match(v => v, e => throw new InvalidOperationException(e.ToString())), ...);
#endif