namespace Trellis.Showcase.Domain.Lifecycle;

using Trellis;

/// <summary>
/// Triggers that drive a <see cref="Aggregates.BankAccount"/> through its lifecycle states
/// (<see cref="Aggregates.AccountStatus.Active"/>, <see cref="Aggregates.AccountStatus.Frozen"/>,
/// <see cref="Aggregates.AccountStatus.Closed"/>).
/// </summary>
/// <remarks>
/// Money operations (deposit/withdraw/transfer) are NOT triggers — they are domain operations
/// guarded by the current <see cref="Aggregates.AccountStatus"/>. Only true lifecycle transitions
/// flow through the state machine, by deliberate design.
/// </remarks>
public partial class AccountTrigger : RequiredEnum<AccountTrigger>
{
    public static readonly AccountTrigger Freeze = new();
    public static readonly AccountTrigger Unfreeze = new();
    public static readonly AccountTrigger Close = new();
}
