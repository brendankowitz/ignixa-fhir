using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The sort-key kinds the compiler can emit joins and value-expressions for. String and Date read from
/// their search-parameter tables via an IsMin/IsMax-flagged row (no aggregation needed); LastUpdated
/// needs no join at all because ResourceSurrogateId already encodes it; ResourceType needs no join
/// either, since the CTE graph already projects the resource's type id as T1; ResourceId needs a join,
/// but to dbo.Resource directly rather than a search-param table, since the CTE graph's own (T1, Sid1)
/// projection doesn't carry the resource's own ResourceId string value; Aggregated covers every other
/// leaf type (Token/Number/Quantity/Reference/Uri) via a MIN/MAX-aggregating derived-table join, since
/// none of those tables carry IsMin/IsMax columns.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name -- 'String' mirrors the FHIR sort-parameter type it represents.
public enum SortKeyKind
{
    String,
    Date,
    LastUpdated,
    ResourceType,
    ResourceId,
    Aggregated,
}
#pragma warning restore CA1720

/// <summary>
/// One _sort key. SearchParamId is null for <see cref="SortKeyKind.LastUpdated"/>,
/// <see cref="SortKeyKind.ResourceType"/> and <see cref="SortKeyKind.ResourceId"/> (none is a
/// search-parameter-table lookup). Table and Column
/// are non-null only for <see cref="SortKeyKind.Aggregated"/> -- String/Date resolve their table/column
/// inline in Emit (StringSearchParam.Text / DateTimeSearchParam.StartDateTime, both fixed), LastUpdated
/// and ResourceType have no column at all (their sort values are the surrogate id and the type id the
/// match set already projects), and ResourceId resolves its column inline in Emit too
/// (dbo.Resource.ResourceId, fixed).
/// </summary>
public sealed record SortKey(
    short? SearchParamId,
    SortKeyKind Kind,
    SortOrder Direction,
    TableDescriptor? Table = null,
    ColumnDescriptor? Column = null);

/// <summary>
/// Which segment of a two-phase missing-value sort a plan computes. Valued makes Keys[0]'s join INNER,
/// so it also gates on the key being present; MissingPrimary drops Keys[0] from the joins and instead
/// requires it absent via NOT EXISTS. Only Keys[0] has a phase; every other key is always a LEFT-JOIN
/// tie-breaker. The phase is a caller input — the executor drives the transition between the two — not
/// something Lower infers from the query.
/// </summary>
public enum SortPhase
{
    Valued,
    MissingPrimary,
}

/// <summary>
/// A compiled _sort, capped at 3 keys. Keys[0] is the primary key that <see cref="SortPhase"/> segments;
/// Keys[1..] are always LEFT-JOIN tie-breakers.
/// </summary>
public sealed record SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase);

/// <summary>
/// The keyset boundary a caller decodes from a continuation token; a null PageSpec means "first page."
/// Boundary carries one value per active key for the current phase — Keys.Count values in Valued,
/// Keys.Count-1 in MissingPrimary (Keys[0] has no value there). Values must already have Emit's
/// ISNULL/sentinel substitution applied, so a decoded token compares equal to a live column. Every
/// non-null field renders as a bound parameter, never an inlined literal, because they are
/// client-controlled input.
/// <para>
/// <see cref="BoundaryResourceTypeId"/> is null for a <em>typeless</em> boundary: the seek then compares
/// only the sort-value key(s) and the surrogate id, with no resource-type component. This is the shape a
/// multi-type search with a custom (search-parameter) <c>_sort</c> needs — the legacy continuation token
/// for such a sort is <c>[sortValue, resourceSurrogateId]</c>, carrying no type slot, and no single type
/// exists to substitute across more than one resource type. It is sound because
/// <c>ResourceSurrogateId</c> is globally unique across resource types (a single
/// <c>dbo.ResourceSurrogateIdUniquifierSequence</c> hands out per-transaction ranges), so a seek on the
/// surrogate id alone is already a total order; the composite <c>(ResourceTypeId, ResourceSurrogateId)</c>
/// key exists only because the table is partitioned on <c>ResourceTypeId</c>. When it is non-null the
/// historical typed seek — <c>(… T1 = @t AND Sid1 &gt; @sid) OR (… T1 &gt; @t)</c> — is emitted unchanged.
/// </para>
/// </summary>
public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,
    SqlParameterRef? BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);
