namespace Ignixa.Domain.Models;

/// <summary>
/// The outcome of an index-only write: resources whose indexes were replaced, and resources skipped because
/// a newer version or a concurrent writer changed them after they were read.
/// </summary>
public sealed record SearchIndexUpdateResult(int Updated, int Conflicts);
