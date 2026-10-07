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
        Interlocked.Exchange(ref _current, handle);
    }
}
