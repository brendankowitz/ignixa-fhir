namespace Ignixa.Application.Features.Search;

/// <summary>
/// Atomically publishes an indexer together with the conformance position from which it was built.
/// </summary>
internal sealed class DefinitionsHandleSlot(DefinitionsHandle initialHandle)
{
    private DefinitionsHandle _current = initialHandle ?? throw new ArgumentNullException(nameof(initialHandle));

    public DefinitionsHandle Current => Volatile.Read(ref _current);

    public void Publish(DefinitionsHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (handle.DefinitionsEventId <= current.DefinitionsEventId)
            {
                return;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, handle, current), current))
            {
                return;
            }
        }
    }
}
