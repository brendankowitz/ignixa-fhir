namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>Accumulates what one CTE or top-level SELECT reads, filters, and combines.</summary>
internal sealed class QueryScope
{
    public List<string> Tables { get; } = [];

    public List<string> Dependencies { get; } = [];

    public List<string> Filters { get; } = [];

    public List<string> Operations { get; } = [];

    public void AddTableOrDependency(string name)
    {
        if (name.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase))
        {
            Tables.Add(name["dbo.".Length..]);
            return;
        }

        // An unqualified name inside these queries is always a reference to another CTE
        // (or to the @FilteredData table variable the shipping engine materializes).
        Dependencies.Add(name);
    }

    public SqlShapeNode ToNode(string name) => new(
        name,
        [.. Tables.Order(StringComparer.Ordinal)],
        [.. Dependencies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        [.. Filters.Order(StringComparer.Ordinal)],
        [.. Operations.Order(StringComparer.Ordinal)]);
}
