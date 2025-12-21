namespace Ignixa.Conformance.Events.Abstractions;

public interface ISourceEventStore
{
    /// <summary>
    /// Appends events to the event store and returns them with their assigned EventIds.
    /// </summary>
    Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken);

    IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken);
    IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken);
}
