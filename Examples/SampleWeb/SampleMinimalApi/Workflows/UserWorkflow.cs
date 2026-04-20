namespace SampleMinimalApi.Workflows;

using SampleMinimalApi.Persistence;
using SampleUserLibrary;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Application boundary for state-changing user use cases (axiom A10).
/// Endpoints never call <see cref="User.TryCreate(FirstName, LastName, EmailAddress, PhoneNumber, Age, CountryCode, string, Maybe{Url})"/>
/// or <see cref="IUserRepository.SaveAsync"/> directly — they dispatch through this workflow so
/// commit semantics (events, AcceptChanges, persistence) live in one place.
/// </summary>
public sealed class UserWorkflow(IUserRepository users, TimeProvider timeProvider)
{
    private readonly IUserRepository _users = users;
    private readonly TimeProvider _timeProvider = timeProvider;

    public Task<Result<User>> RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dto);
        _ = _timeProvider; // injected to demonstrate TimeProvider seam; not used by this aggregate
        return User.TryCreate(dto.FirstName, dto.LastName, dto.Email, dto.Phone, dto.Age, dto.Country, dto.Password, dto.Website)
            .TapAsync(user => CommitAsync(user, cancellationToken));
    }

    private Task<Result> CommitAsync(User user, CancellationToken cancellationToken) =>
        _users.SaveAsync(user, cancellationToken);
}
