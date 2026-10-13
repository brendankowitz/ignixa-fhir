using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>
/// An in-memory event store with the durable store's optimistic contract: an append succeeds only at the
/// current last event id. Events queued in <see cref="OtherWriters"/> are committed by "another instance"
/// at the start of each append attempt, one per attempt, so the attempt loses the race.
/// </summary>
internal sealed class InMemoryEventLog : ISourceEventStore
{
    public List<SourceEvent> Events { get; } = [];

    public List<IReadOnlyList<SourceEvent>> Appends { get; } = [];

    public Queue<object> OtherWriters { get; } = [];

    public int AppendAttempts { get; private set; }

    /// <summary>Runs after each successful append.</summary>
    public Action? AfterAppend { get; set; }

    public long LastEventId => Events.Count == 0 ? 0 : Events[^1].EventId;

    /// <summary>Commits an event that <paramref name="state"/> has already applied.</summary>
    public long Seed(ConformanceState state, object data)
    {
        var sourceEvent = Commit(data);
        state.ApplyAndTrack(sourceEvent);
        return sourceEvent.EventId;
    }

    public Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken) =>
        AppendAsync(events, LastEventId, cancellationToken);

    public Task<IReadOnlyList<SourceEvent>> AppendAsync(
        IEnumerable<NewSourceEvent> events,
        long expectedLastEventId,
        CancellationToken cancellationToken)
    {
        AppendAttempts++;
        if (OtherWriters.TryDequeue(out var winner))
        {
            Commit(winner);
        }

        if (expectedLastEventId != LastEventId)
        {
            throw new SourceEventConcurrencyException(expectedLastEventId, LastEventId);
        }

        SourceEvent[] appended = events
            .Select((sourceEvent, index) => new SourceEvent(LastEventId + index + 1, sourceEvent.StreamId, sourceEvent.EventType, sourceEvent.Data, DateTimeOffset.UtcNow))
            .ToArray();
        Events.AddRange(appended);
        Appends.Add(appended);
        AfterAppend?.Invoke();
        return Task.FromResult<IReadOnlyList<SourceEvent>>(appended);
    }

    public IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        Events.ToArray().ToAsyncEnumerable();

    public IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken) =>
        Events.Where(sourceEvent => sourceEvent.EventId > afterEventId).ToArray().ToAsyncEnumerable();

    public IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken) =>
        Events.Where(sourceEvent => sourceEvent.StreamId == streamId).ToArray().ToAsyncEnumerable();

    private SourceEvent Commit(object data)
    {
        var sourceEvent = ConformanceEvents.At(LastEventId + 1, data);
        Events.Add(sourceEvent);
        return sourceEvent;
    }
}
