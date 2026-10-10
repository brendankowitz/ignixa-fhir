namespace Ignixa.Domain.Models;

/// <summary>
/// A resource transaction that has been allocated but has not completed, identified by the first value of
/// its surrogate id range.
/// </summary>
public sealed record IncompleteTransaction(long TransactionId, DateTime CreateDate, DateTime HeartbeatDate);
