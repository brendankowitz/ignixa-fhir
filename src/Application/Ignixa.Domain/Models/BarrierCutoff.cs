namespace Ignixa.Domain.Models;

/// <summary>
/// A tenant's position when the conformance barrier was raised: the newest allocated transaction and the
/// highest surrogate id allocated or written so far. Either is <c>-1</c> when the tenant has none.
/// </summary>
public sealed record BarrierCutoff(long TransactionId, long SurrogateId);
