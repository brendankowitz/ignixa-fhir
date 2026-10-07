using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Search;

internal sealed class ConformanceDefinitionsSnapshotSlot
{
    private ConformanceDefinitionsSnapshot _current;

    public ConformanceDefinitionsSnapshot Current => Volatile.Read(ref _current);
    public ISearchParameterDefinitionManager ExtractionDefinitions { get; }
    public ISearchParameterDefinitionManager SearchableDefinitions { get; }

    public ConformanceDefinitionsSnapshotSlot(ConformanceDefinitionsSnapshot initialSnapshot)
    {
        _current = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
        ExtractionDefinitions =
            new CurrentSearchParameterDefinitionManager(() => Volatile.Read(ref _current).ExtractionDefinitions);
        SearchableDefinitions =
            new CurrentSearchParameterDefinitionManager(() => Volatile.Read(ref _current).SearchableDefinitions);
    }

    public void Publish(ConformanceDefinitionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (snapshot.Generation <= current.Generation)
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
