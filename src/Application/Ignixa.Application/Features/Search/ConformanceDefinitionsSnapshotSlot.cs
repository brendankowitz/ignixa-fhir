namespace Ignixa.Application.Features.Search;

/// <summary>
/// The currently visible definition set for one tenant and FHIR version. Publication is monotonic, so a
/// slower refresh never replaces a newer set.
/// </summary>
internal sealed class ConformanceDefinitionsSnapshotSlot
{
    private ConformanceDefinitionsSnapshot _current;

    public ConformanceDefinitionsSnapshotSlot(ConformanceDefinitionsSnapshot initialSnapshot)
    {
        _current = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
    }

    public ConformanceDefinitionsSnapshot Current => Volatile.Read(ref _current);

    public static bool IsNewer(ConformanceDefinitionsSnapshot candidate, ConformanceDefinitionsSnapshot current) =>
        candidate.Generation > current.Generation ||
        (candidate.Generation == current.Generation &&
         candidate.PublicationSequence > current.PublicationSequence);

    public void Publish(ConformanceDefinitionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (!IsNewer(snapshot, current))
            {
                return;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, snapshot, current), current))
            {
                return;
            }
        }
    }
}
