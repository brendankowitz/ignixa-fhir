namespace Ignixa.Domain.Models;

/// <summary>
/// The outcome of an index-only write: resources whose indexes were replaced, and resources skipped because
/// a newer version or a concurrent writer changed them after they were read.
/// </summary>
public sealed record SearchIndexUpdateResult(int Updated, int Conflicts)
{
    /// <summary>
    /// Set when nothing was written because the tenant's conformance barrier, this value, is above the
    /// definitions the batch was extracted with.
    /// </summary>
    public long? RefusingBarrier { get; init; }

    public static SearchIndexUpdateResult Refused(long barrier) => new(0, 0) { RefusingBarrier = barrier };
}
