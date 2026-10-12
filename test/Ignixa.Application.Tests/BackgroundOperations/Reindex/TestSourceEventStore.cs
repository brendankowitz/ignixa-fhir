using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>
/// An event store that commits every append with increasing ids, and reads back only <see cref="Unread"/>:
/// the events another instance committed and this one has not applied yet.
/// </summary>
internal sealed class TestSourceEventStore(long firstEventId = 2) : ISourceEventStore
{
    private long _nextEventId = firstEventId;

    public List<SourceEvent> Appended { get; } = [];

    public List<SourceEvent> Unread { get; } = [];

    public bool FailAppends { get; set; }

    /// <summary>Runs before an append commits, so a test can hold it open.</summary>
    public Func<Task>? BeforeAppend { get; set; }

    public Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken) =>
        AppendAsync(events, expectedLastEventId: -1, cancellationToken);

    public async Task<IReadOnlyList<SourceEvent>> AppendAsync(
        IEnumerable<NewSourceEvent> events,
        long expectedLastEventId,
        CancellationToken cancellationToken)
    {
        if (BeforeAppend is { } beforeAppend)
        {
            await beforeAppend();
        }

        if (FailAppends)
        {
            throw new InvalidOperationException("event store unavailable");
        }

        SourceEvent[] committed = events
            .Select(evt => new SourceEvent(_nextEventId++, evt.StreamId, evt.EventType, evt.Data, DateTimeOffset.UtcNow))
            .ToArray();
        Appended.AddRange(committed);
        return committed;
    }

    public IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken) => Unread.ToAsyncEnumerable();

    public IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken) =>
        Unread.Where(evt => evt.EventId > afterEventId).ToAsyncEnumerable();

    public IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken) =>
        Unread.Where(evt => evt.StreamId == streamId).ToAsyncEnumerable();
}
