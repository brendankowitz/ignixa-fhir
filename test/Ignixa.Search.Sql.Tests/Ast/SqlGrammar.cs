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

    /// <summary>Counts the nodes of a given ScriptDom type in the parsed tree — for object-model assertions.</summary>
    public static int Count<T>(TSqlFragment fragment)
        where T : TSqlFragment
    {
        var counter = new NodeCounter<T>();
        fragment.Accept(counter);
        return counter.Count;
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
}
