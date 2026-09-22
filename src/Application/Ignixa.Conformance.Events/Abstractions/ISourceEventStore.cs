namespace Ignixa.Conformance.Events.Abstractions;

public interface ISourceEventStore
{
    /// <summary>
    /// Appends events to the event store and returns them with their assigned EventIds.
    /// </summary>
    Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken);

    /// <summary>
    /// Appends only if the durable global event position still matches the validated snapshot.
    /// The position check and insertion must share one serialized transaction.
    /// </summary>
    /// <exception cref="SourceEventConcurrencyException">Another append committed after the snapshot.</exception>
    Task<IReadOnlyList<SourceEvent>> AppendAsync(
        IEnumerable<NewSourceEvent> events,
        long expectedLastEventId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This event store does not support atomic expected-position appends.");

    IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken);
}
