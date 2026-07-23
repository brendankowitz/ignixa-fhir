namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// A dialect-independent description of what a search query actually asks the database for: which
/// index tables it reads, which semantic filters it applies, and which set operations combine them.
///
/// The two dialects under comparison express the same semantics with different syntax -- the shipping
/// engine folds an intersection into the next source CTE as a correlated EXISTS, the compiler emits a
/// separate INNER JOIN CTE -- so CTE-by-CTE equality is meaningless. The multisets below are collected
/// over the whole statement, which makes those encoding choices invisible while keeping every semantic
/// difference (an extra table read, a missing filter, a different set operation) visible.
/// </summary>
public sealed record SqlShape(
    IReadOnlyList<SqlShapeNode> Nodes,
    IReadOnlyDictionary<string, int> Tables,
    IReadOnlyDictionary<string, int> Filters,
    IReadOnlyDictionary<string, int> Operations)
{
    public int CteCount => Nodes.Count(n => n.Name.StartsWith("cte", StringComparison.OrdinalIgnoreCase));

    public string Describe() => string.Join("\n", Nodes.Select(n => n.Describe()));
}
