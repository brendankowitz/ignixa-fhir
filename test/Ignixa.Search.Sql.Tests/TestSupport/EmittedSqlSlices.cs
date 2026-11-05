using Ignixa.Search.Sql.Builders;
using Shouldly;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Named slices of emitted SQL text that tests assert against.</summary>
internal static class EmittedSqlSlices
{
    /// <summary>The <c>cteMatchPage AS ( … )</c> block, from its label through its closing paren.</summary>
    public static string MatchPageBody(string sql) => CteBlock(sql, SqlLabels.MatchPage);

    /// <summary>The <c>incN AS ( … )</c> block, from its label through its closing paren.</summary>
    public static string IncludeStageBody(string sql, int index) => CteBlock(sql, SqlLabels.IncludeLabel(index));

    /// <summary>The outer SELECT of an includes count, through the end of the statement.</summary>
    public static string IncludesCountSelect(string sql)
    {
        var start = sql.IndexOf("SELECT COUNT_BIG(*) FROM (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..];
    }

    private static string CteBlock(string sql, string label)
    {
        var start = sql.IndexOf($"{label} AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);

        // A CTE closes with ")" in the first column; every paren inside the body is indented.
        var end = sql.IndexOf("\n)", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..(end + 2)];
    }
}
