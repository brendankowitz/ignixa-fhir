namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// One named CTE (or the final SELECT) of a canonicalized query, kept for the human-readable side of
/// the differential report. Comparison itself runs on <see cref="SqlShape"/>'s whole-query multisets,
/// because the two dialects draw CTE boundaries in different places for the same semantics.
/// </summary>
public sealed record SqlShapeNode(
    string Name,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Filters,
    IReadOnlyList<string> Operations)
{
    public string Describe()
    {
        var parts = new List<string>();
        if (Tables.Count > 0)
        {
            parts.Add(string.Join("+", Tables));
        }

        if (Dependencies.Count > 0)
        {
            parts.Add($"<-{string.Join(",", Dependencies)}");
        }

        if (Filters.Count > 0)
        {
            parts.Add(string.Join(" ", Filters));
        }

        if (Operations.Count > 0)
        {
            parts.Add($"[{string.Join(",", Operations)}]");
        }

        return $"{Name} = {string.Join("  ", parts)}";
    }
}
