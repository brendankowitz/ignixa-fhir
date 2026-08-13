using System.Globalization;
using System.Text;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The shape of a CTE's inner SELECT, rendered by <see cref="Render"/> rather than assembled by hand at each
/// call site.
/// </summary>
/// <remarks>
/// The emitters used to interpolate their own indentation, newlines and <c>" AND "</c> separators, which made
/// the whitespace load-bearing at a dozen sites and let genuine defects hide in it — an absent WHERE clause
/// leaving a trailing newline, or an empty clause list interpolating to <c>WHERE </c> and failing to parse.
/// Stating the parts and rendering them once removes both.
///
/// The guarantee is about the clause <em>list</em>, not the clause <em>strings</em>: an empty list emits no
/// WHERE at all, but an empty or blank entry would still render <c>WHERE  AND …</c>. That is why
/// <see cref="Ast.PlanShapeValidator"/> keeps its empty-type-list guard — a node whose resource-type list is
/// empty renders to an empty clause string, which this type cannot detect. Do not delete that guard as
/// redundant.
///
/// <see cref="Joins"/> entries are pre-rendered blocks, including their own indentation and any interior
/// newlines. Join formatting varies more than the rest of the statement (correlated <c>ON</c>/<c>AND</c>
/// continuations line up under their own join), and forcing it into this model would need more escape
/// hatches than it removed.
/// </remarks>
internal sealed record SelectBlock
{
    /// <summary>The projected columns, without the leading <c>SELECT</c>.</summary>
    public required string Columns { get; init; }

    /// <summary>The table or CTE being read, without the leading <c>FROM</c>.</summary>
    public required string From { get; init; }

    /// <summary>Whether to emit <c>SELECT DISTINCT</c>.</summary>
    public bool Distinct { get; init; }

    /// <summary>A row cap, rendered between <c>SELECT</c> and the columns as <c>TOP (n) </c>.</summary>
    public int? Top { get; init; }

    /// <summary>Pre-rendered join blocks, each already indented and free of a trailing newline.</summary>
    public IReadOnlyList<string> Joins { get; init; } = [];

    /// <summary>The ANDed WHERE clauses. Empty emits no WHERE at all.</summary>
    public IReadOnlyList<string> Where { get; init; } = [];

    /// <summary>How <see cref="Where"/> is laid out.</summary>
    public WhereLayout WhereLayout { get; init; } = WhereLayout.Inline;

    /// <summary>The ordering, without the leading <c>ORDER BY</c>.</summary>
    public string? OrderBy { get; init; }

    /// <summary>A trailing paging clause such as <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c>.</summary>
    public string? Offset { get; init; }

    /// <summary>The indentation every top-level keyword sits at.</summary>
    public string Indent { get; init; } = "    ";

    /// <summary>
    /// The <c>TOP (n) </c> fragment, trailing space included, or empty. Shared with the match-page emitter,
    /// which assembles its SELECT through <see cref="SqlTextWriter"/> rather than this type but must render
    /// the cap identically — the trailing space is stated here once instead of at each call site.
    /// </summary>
    internal static string RenderTop(int? top)
        => top is { } n ? string.Create(CultureInfo.InvariantCulture, $"TOP ({n}) ") : string.Empty;

    /// <summary>Renders the statement. No trailing newline: the caller decides what follows.</summary>
    public string Render()
    {
        var sql = new StringBuilder();
        sql.Append(Indent).Append("SELECT ");
        if (Distinct)
        {
            sql.Append("DISTINCT ");
        }

        sql.Append(RenderTop(Top)).Append(Columns);
        sql.Append('\n').Append(Indent).Append("FROM ").Append(From);

        foreach (var join in Joins)
        {
            sql.Append('\n').Append(join);
        }

        AppendWhere(sql);

        if (OrderBy is not null)
        {
            sql.Append('\n').Append(Indent).Append("ORDER BY ").Append(OrderBy);
        }

        if (Offset is not null)
        {
            sql.Append('\n').Append(Indent).Append(Offset);
        }

        return sql.ToString();
    }

    private void AppendWhere(StringBuilder sql)
    {
        if (Where.Count == 0)
        {
            return;
        }

        sql.Append('\n').Append(Indent).Append("WHERE ").Append(Where[0]);

        for (var i = 1; i < Where.Count; i++)
        {
            sql.Append(WhereLayout == WhereLayout.Inline ? " AND " : $"\n{Indent}  AND ").Append(Where[i]);
        }
    }
}
