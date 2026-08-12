#pragma warning disable CA1724

using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.CteEmitter;
using static Ignixa.Search.Sql.Builders.ShapeEmitter;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Turns a <see cref="QueryPlan"/> into parameterized T-SQL text, deterministically — the same plan
/// always emits byte-identical SQL. Every <see cref="CteDefinition"/> entry becomes its own named CTE, so
/// Match can reference any nesting depth without special-casing the outer SELECT. No user value is ever
/// inlined: every <see cref="SqlParameterRef"/> becomes a named @pN parameter.
/// </summary>
internal static class SqlBuilder
{
    /// <summary>
    /// Renders a plan to SQL and its bound parameters by selecting one of three terminal shapes and
    /// delegating to its emitter: a COUNT_BIG SELECT when CountOnly, a plain (T1, Sid1) select (with
    /// optional sort/paging) when there are no includes, or a match-page CTE plus per-stage include CTEs
    /// unioned into a (T1, Sid1, IsMatch, IsPartial) result.
    /// </summary>
    public static EmittedSql Run(QueryPlan plan, EmitOptions? options = null)
    {
        QueryPlanValidator.Validate(plan);
        PlanValidator.Validate(plan);

        var parameters = new List<EmittedSqlParameter>();
        var writer = new SqlTextWriter(options?.IncludeTextRanges ?? false);
        var visibility = plan.EffectiveVisibility;
        var cteBodies = EmitCteBodies(plan, parameters, visibility);

        if (plan.CountOnly)
        {
            EmitCountOnlyShape(plan, writer, cteBodies, parameters);
        }
        else if (plan.Includes is { Count: > 0 } includes)
        {
            EmitIncludesShape(plan, includes, writer, cteBodies, parameters, visibility);
        }
        else
        {
            EmitMatchOnlyShape(plan, writer, cteBodies, parameters, visibility);
        }

        return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
    }

    /// <summary>Writes a WHERE clause at the given indent, or nothing when there are no clauses.</summary>
    internal static void WriteWhereSection(SqlTextWriter writer, List<string> clauses, int? seekClauseIndex, string indent)
    {
        if (clauses.Count == 0)
        {
            return;
        }

        writer.Append($"\n{indent}WHERE ");
        using (writer.Section(Where, SqlRangeKind.Where))
        {
            WriteAndJoinedClauses(writer, clauses, seekClauseIndex);
        }
    }
}
