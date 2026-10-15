namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Thrown after a SQL allocation is completed as failed because its search indexes were extracted
/// from definitions older than the tenant's accepted conformance position.
/// </summary>
public sealed class StaleConformanceDefinitionsException(
    long transactionId,
    long definitionsEventId,
    long minimumAcceptedDefinitionsEventId)
    : Exception(
        $"Transaction {transactionId} used conformance definitions at EventId {definitionsEventId}, " +
        $"but the tenant requires EventId {minimumAcceptedDefinitionsEventId} or later.")
{
    public long TransactionId { get; } = transactionId;

    public long DefinitionsEventId { get; } = definitionsEventId;

    public long MinimumAcceptedDefinitionsEventId { get; } = minimumAcceptedDefinitionsEventId;
}
