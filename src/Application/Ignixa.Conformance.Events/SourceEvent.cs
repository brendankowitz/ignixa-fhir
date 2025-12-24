namespace Ignixa.Conformance.Events;

public record SourceEvent(
    long EventId,
    string StreamId,
    string EventType,
    object Data,
    DateTimeOffset Timestamp,
    long TransactionId = 0);

public record NewSourceEvent(
    string StreamId,
    string EventType,
    object Data);
