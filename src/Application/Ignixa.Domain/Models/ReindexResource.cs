namespace Ignixa.Domain.Models;

/// <summary>
/// A current resource read for reindexing, paired with the immutable surrogate identity the
/// index-only update must match. The wrapped resource carries the indexes from its own definitions
/// generation when it is passed back to <see cref="Abstractions.IReindexStore"/>.
/// </summary>
public sealed record ReindexResource(ResourceWrapper Resource, long ResourceSurrogateId);
