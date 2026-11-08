namespace Ignixa.Domain.Models;

/// <summary>
/// One page of contiguous surrogate id ranges. <see cref="NextStartAfter"/> is the id the next page starts
/// after, or <see langword="null"/> when the last range already reaches the requested upper bound.
/// </summary>
public sealed record SurrogateIdRangePage(IReadOnlyList<SurrogateIdRange> Ranges, long? NextStartAfter);
