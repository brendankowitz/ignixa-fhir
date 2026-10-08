namespace Ignixa.Domain.Models;

/// <summary>
/// The current state of an instance selected for synchronous reindexing.
/// </summary>
public sealed record ReindexCurrentResource(ReindexResource? Resource, bool IsDeleted);
