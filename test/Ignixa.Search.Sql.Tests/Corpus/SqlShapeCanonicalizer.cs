using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Turns either dialect's SQL text into a <see cref="SqlShape"/> using the real SQL Server grammar.
/// Literals are erased to their kind (&lt;n&gt;, &lt;s&gt;, @p) so surrogate ids and user values never
/// drive a comparison, and column-to-column comparisons are classified as correlation plumbing rather
/// than semantic filters -- the two dialects correlate rows differently for identical semantics.
/// </summary>
public static class SqlShapeCanonicalizer
{
    public static SqlShape Canonicalize(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            var detail = string.Join("; ", errors.Select(e => $"L{e.Line}:{e.Column} {e.Message}"));
            throw new FormatException($"SQL did not parse: {detail}");
        }

        var nodes = new List<SqlShapeNode>();
        var collector = new StatementCollector(nodes);
        fragment.Accept(collector);

        return new SqlShape(
            nodes,
            Tally(nodes.SelectMany(Tables).Select(ForComparison)),
            Tally(nodes.SelectMany(Filters).Select(ForComparison)),
            Tally(nodes.SelectMany(n => n.Operations)));
    }

    /// <summary>
    /// Row hydration is where the two dialects part company by design: the shipping engine's terminal
    /// SELECT joins dbo.Resource to fetch RawResource, the compiler returns identity columns and leaves
    /// the fetch to its caller. Only that join is excluded -- the rest of the terminal SELECT still
    /// counts, because it is where the compiler puts the resource-column filters (_id, _type,
    /// _lastUpdated) it lifts out of the CTE graph.
    /// </summary>
    private static bool IsHydration(SqlShapeNode node) => node.Name.StartsWith("select", StringComparison.Ordinal);

    private static IEnumerable<string> Tables(SqlShapeNode node)
        => IsHydration(node) ? node.Tables.Where(t => t != "Resource") : node.Tables;

    private static IEnumerable<string> Filters(SqlShapeNode node)
        => IsHydration(node)
            ? node.Filters.Where(f => !f.StartsWith("IsHistory", StringComparison.Ordinal) && !f.StartsWith("IsDeleted", StringComparison.Ordinal))
            : node.Filters;

    /// <summary>
    /// Erases two further encoding choices before comparison. Whether a read sits in a subquery or in a
    /// CTE of its own is the difference between the shipping engine's `NOT IN (SELECT ...)` and the
    /// compiler's dedicated except CTE -- same tables, same filters. And whether a value arrives as a
    /// bound parameter or an inlined literal is a parameterization policy, not a question the query asks
    /// the database differently; the value itself is already erased on both sides.
    /// </summary>
    private static string ForComparison(string item)
    {
        var stripped = item.StartsWith("sub:", StringComparison.Ordinal) ? item["sub:".Length..] : item;
        return stripped.Replace("@p", "<v>", StringComparison.Ordinal).Replace("<n>", "<v>", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, int> Tally(IEnumerable<string> values)
    {
        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            tally[value] = tally.TryGetValue(value, out var count) ? count + 1 : 1;
        }

        return tally;
    }

    /// <summary>Walks named CTEs and top-level SELECTs, turning each into one shape node.</summary>
    private sealed class StatementCollector(List<SqlShapeNode> nodes) : TSqlFragmentVisitor
    {
        private int _selectOrdinal;

        public override void Visit(CommonTableExpression node)
        {
            var scope = new QueryScope();
            QueryWalker.Walk(node.QueryExpression, scope);
            nodes.Add(scope.ToNode(node.ExpressionName.Value));
        }

        public override void Visit(SelectStatement node)
        {
            // A SELECT that owns CTEs contributes only its own body here; each CTE is visited separately.
            var scope = new QueryScope();
            QueryWalker.Walk(node.QueryExpression, scope);
            nodes.Add(scope.ToNode($"select{_selectOrdinal++}"));
        }
    }
}
