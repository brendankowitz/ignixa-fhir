namespace Ignixa.Domain.Models;

/// <summary>
/// An inclusive surrogate id range of one resource type and the number of current resources in it. Ranges
/// may cover id gaps.
/// </summary>
public sealed record SurrogateIdRange(long Start, long End, long ResourceCount);
