namespace Ignixa.Conformance.Events;

public sealed class SourceEventConcurrencyException(long expectedEventId, long actualEventId)
    : Exception($"Conformance changed after validation: expected event position {expectedEventId}, actual {actualEventId}. Refresh conformance state and retry activation.")
{
    public long ExpectedEventId { get; } = expectedEventId;

    public long ActualEventId { get; } = actualEventId;
}
