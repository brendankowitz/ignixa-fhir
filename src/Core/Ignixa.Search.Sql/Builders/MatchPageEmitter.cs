using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;
using static Ignixa.Search.Sql.Builders.SortEmitter;
using static Ignixa.Search.Sql.Builders.SqlBuilder;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the cteMatchPage and cteMatchSeed CTEs and the WHERE clauses selecting a page of match rows.</summary>
internal static class MatchPageEmitter
{
    /// <summary>
    /// Renders the cteMatchPage CTE body: the same match row set the no-includes shape selects directly,
    /// named so the include stages and the UNION ALL can each reference it without re-deriving it.
    /// </summary>
    internal static CteBody EmitMatchPage(MatchPageSpec spec, List<EmittedSqlParameter> parameters)
    {
        var top = spec.Top is { } n ? $"TOP ({n}) " : string.Empty;
        var sortJoins = EmitSortJoins(spec.Sort);

        // An includes-only page never orders by the sort key, so the match CTE projects no SortValueN columns.
        // The sort JOINs stay: the Valued phase's INNER join bounds the match set to rows that have the sort
        // value, and the include stages seed from that bounded set.
        var sortColumns = spec.IncludesOnly ? string.Empty : EmitSortSelectColumns(spec.Sort);

        // Projection is handled in the UNION ALL assembly, not here, so includesProjection is false.
        var resourceJoin = spec.OuterPredicate is not null || spec.SearchParameterHash is not null
            ? "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1"
            : string.Empty;

        // A CTE's own ORDER BY is legal only alongside TOP or OFFSET/FETCH (SQL Server Msg 1033). The outer
        // UNION ALL's ORDER BY is a top-level SELECT and always legal regardless.
        var cteOrderBy = spec.Top is not null || spec.OffsetPage is not null
            ? $"\n    ORDER BY {EmitOrderBy(spec.Sort)}"
            : string.Empty;

        var whereClauses = BuildMatchWhereClauses(spec, parameters, out var seekClauseIndex);

        var offsetClause = spec.OffsetPage is { } offsetPage
            ? $"\n    OFFSET {EmitParam(new SqlParameterRef(offsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(offsetPage.FetchCount), parameters)} ROWS ONLY"
            : string.Empty;

        var writer = new SqlTextWriter(recordRanges: true);
        writer.Append(
            $"    SELECT {top}m.T1, m.Sid1{sortColumns}\n" +
            $"    FROM {CteLabel(spec.Root.Index)} m{sortJoins}{resourceJoin}");
        WriteWhereSection(writer, whereClauses, seekClauseIndex, indent: "    ");
        writer.Append(cteOrderBy);
        writer.Append(offsetClause);

        return new CteBody(writer.ToString(), writer.Ranges);
    }

    /// <summary>
    /// Renders the cteMatchSeed CTE body: the <see cref="MatchPage"/> rows that are genuinely ON the page,
    /// with its has-more probe row trimmed off, in the match page's own order.
    /// </summary>
    /// <remarks>
    /// An over-fetching page returns Limit + 1 rows so the caller can detect a further page, then discards
    /// that last row. Seeding include stages from the full match page resolves includes for the discarded
    /// row too, and nothing downstream can undo that: the assembled bundle records no link from an included
    /// resource back to the match it came from, and an include reachable from BOTH a kept match and the
    /// probe row must survive. Trimming has to happen here, where the ordering is still known.
    ///
    /// TOP over the already-paged CTE rather than a narrowed FETCH NEXT, because T-SQL rejects TOP and
    /// OFFSET/FETCH in the same query (error 10741) and the match page must still return its probe row.
    /// The ORDER BY repeats the match page's ordering through the SortValueN columns it projects — take
    /// "first Limit rows" under any other ordering and the wrong row is dropped.
    /// </remarks>
    internal static CteBody EmitMatchSeed(CteDefinition.MatchSeed seed)
    {
        var offsetPage = seed.Spec.OffsetPage
            ?? throw new NotSupportedException("MatchSeed requires an OffsetPage.");

        return new CteBody(
            $"    SELECT TOP ({offsetPage.Limit}) T1, Sid1\n" +
            $"    FROM {MatchPage}\n" +
            $"    ORDER BY {EmitSortValueOrderBy(seed.Spec.Sort)}");
    }

    /// <summary>
    /// Builds the WHERE clauses selecting the page of match rows, shared by the no-includes shape and the
    /// includes shape's match-page CTE, and reports which clause is the keyset seek. The two shapes must agree
    /// on every clause or a paged search diverges from the same search with an _include. Include stages get
    /// none: their rows are reached by reference, not surrogate id or hash.
    /// </summary>
    internal static List<string> BuildMatchWhereClauses(
        MatchPageSpec spec,
        List<EmittedSqlParameter> parameters,
        out int? seekClauseIndex)
    {
        var clauses = new List<string>();
        seekClauseIndex = null;

        if (spec.OuterPredicate is not null)
        {
            clauses.Add(EmitPredicate(spec.OuterPredicate, parameters, ResourceJoinQualifier));
        }

        if (spec.Sort is { Phase: SortPhase.MissingPrimary } missingPhaseSort)
        {
            clauses.Add(EmitMissingPrimaryFilter(missingPhaseSort));
        }

        if (spec.Page is { } page)
        {
            seekClauseIndex = clauses.Count;
            clauses.Add(EmitSeekPredicate(spec.Sort, page, parameters));
        }

        if (spec.SurrogateRange is { } range)
        {
            AppendSurrogateRangeClauses(clauses, range, parameters);
        }

        if (spec.SearchParameterHash is { } hash)
        {
            clauses.Add(EmitSearchParameterHashClause(hash, parameters));
        }

        return clauses;
    }

    /// <summary>
    /// Renders the reindex-eligibility filter for one search-parameter hash. The IS NULL disjunct qualifies
    /// resources that have never been indexed (no hash because they pre-date the feature).
    /// </summary>
    internal static string EmitSearchParameterHashClause(SqlParameterRef hash, List<EmittedSqlParameter> parameters)
        => $"(r.SearchParamHash IS NULL OR r.SearchParamHash <> {EmitParam(hash, parameters)})";

    /// <summary>
    /// Appends the inclusive surrogate-id window to a shape's WHERE clause list. Extracted because omitting it
    /// in one shape is silent: an $export worker would read outside its partition and duplicate exported
    /// resources with no error. Include stages deliberately skip it (their rows are reached by reference).
    /// </summary>
    internal static void AppendSurrogateRangeClauses(
        List<string> clauses,
        SurrogateIdRange range,
        List<EmittedSqlParameter> parameters)
    {
        clauses.Add($"m.Sid1 >= {EmitParam(range.Start, parameters)}");
        clauses.Add($"m.Sid1 <= {EmitParam(range.End, parameters)}");
    }
}
