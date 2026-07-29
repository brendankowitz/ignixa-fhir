using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit.Sdk;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Parses emitted T-SQL with the real SQL Server grammar (ScriptDom) so tests can assert the SQL is
/// syntactically well-formed and inspect its parsed object model — instead of only comparing exact text.
/// Grammar checks survive cosmetic refactors (whitespace, alias renames) that byte-exact goldens do not.
/// </summary>
internal static class SqlGrammar
{
    /// <summary>Parses the SQL, throwing with the parse errors if it is not valid T-SQL.</summary>
    public static TSqlFragment Parse(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            var detail = string.Join("\n", errors.Select(e => $"  L{e.Line}:{e.Column} {e.Message}"));
            throw new XunitException($"Emitted SQL is not valid T-SQL:\n\n{sql}\n\nParse errors:\n{detail}");
        }

        return fragment;
    }

    /// <summary>Asserts the SQL parses cleanly under the SQL Server grammar.</summary>
    public static void AssertValid(string sql) => Parse(sql);

    /// <summary>
    /// Asserts every unqualified table reference in the statement names a CTE the statement itself defines.
    /// </summary>
    /// <remarks>
    /// The emitter's only unqualified table references are CTE names — every real table is written
    /// <c>dbo.X</c> — so an unqualified name with no matching <c>CommonTableExpression</c> is a reference to
    /// a CTE that was never emitted, which SQL Server rejects at execution with Msg 207 (invalid object
    /// name). The grammar check alone cannot see this: an undefined CTE reference parses perfectly.
    /// </remarks>
    public static void AssertEveryReferencedCteIsDefined(string sql)
    {
        var collector = new TableReferenceCollector();
        Parse(sql).Accept(collector);

        var undefined = collector.UnqualifiedTables
            .Where(name => !collector.DefinedCtes.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (undefined.Count > 0)
        {
            throw new XunitException(
                $"Emitted SQL references undefined CTE(s): {string.Join(", ", undefined)}.\n" +
                $"Defined: {string.Join(", ", collector.DefinedCtes)}\n\n{sql}");
        }
    }

    /// <summary>Counts the nodes of a given ScriptDom type in the parsed tree — for object-model assertions.</summary>
    public static int Count<T>(TSqlFragment fragment)
        where T : TSqlFragment
    {
        var counter = new NodeCounter<T>();
        fragment.Accept(counter);
        return counter.Count;
    }

    /// <summary>Collects every node of a given ScriptDom type in the parsed tree, for assertions on their properties.</summary>
    public static List<T> FindAll<T>(TSqlFragment fragment)
        where T : TSqlFragment
    {
        var collector = new NodeCollector<T>();
        fragment.Accept(collector);
        return collector.Nodes;
    }

    private sealed class TableReferenceCollector : TSqlFragmentVisitor
    {
        public HashSet<string> DefinedCtes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> UnqualifiedTables { get; } = [];

        public override void Visit(TSqlFragment node)
        {
            switch (node)
            {
                case CommonTableExpression cte:
                    DefinedCtes.Add(cte.ExpressionName.Value);
                    break;

                case NamedTableReference { SchemaObject: { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null } schemaObject }:
                    UnqualifiedTables.Add(schemaObject.BaseIdentifier.Value);
                    break;
            }
        }
    }

    private sealed class NodeCounter<T> : TSqlFragmentVisitor
        where T : TSqlFragment
    {
        public int Count { get; private set; }

        public override void Visit(TSqlFragment node)
        {
            if (node is T)
            {
                Count++;
            }
        }
    }

    private sealed class NodeCollector<T> : TSqlFragmentVisitor
        where T : TSqlFragment
    {
        public List<T> Nodes { get; } = [];

        public override void Visit(TSqlFragment node)
        {
            if (node is T typed)
            {
                Nodes.Add(typed);
            }
        }
    }
}
