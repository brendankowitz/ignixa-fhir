using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The sort-key kinds the compiler emits joins/value-expressions for. String/Date read their search-param
/// tables via an IsMin/IsMax-flagged row; LastUpdated and ResourceType need no join (surrogate id and the
/// T1 projection carry them); ResourceId joins dbo.Resource; Aggregated covers the other leaf types via a
/// MIN/MAX-aggregating derived-table join.
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
/// One _sort key. SearchParamId is null for LastUpdated, ResourceType and ResourceId (none is a
/// search-param-table lookup). Table and Column are non-null only for <see cref="SortKeyKind.Aggregated"/>;
/// the other kinds resolve their column inline in Emit or have none (surrogate id / type id).
/// </summary>
public sealed record SortKey(
    short? SearchParamId,
    SortKeyKind Kind,
    SortOrder Direction,
    TableDescriptor? Table = null,
    ColumnDescriptor? Column = null);

/// <summary>
/// Which segment of a two-phase missing-value sort a plan computes. Valued makes Keys[0]'s join INNER (also
/// gating on presence); MissingPrimary drops Keys[0] and requires it absent via NOT EXISTS. Only Keys[0]
/// has a phase; other keys are always LEFT-JOIN tie-breakers. The phase is a caller input, not inferred.
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
/// The keyset boundary decoded from a continuation token (null = first page). Boundary carries one value per
/// active key for the phase (Keys.Count in Valued, Keys.Count-1 in MissingPrimary), already ISNULL/sentinel-
/// substituted and bound as parameters. BoundaryResourceTypeId null is a typeless boundary for a multi-type
/// custom _sort (sound: ResourceSurrogateId is globally unique); SqlBuilder.Run rejects a type/sort mismatch.
/// </summary>
public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,
    SqlParameterRef? BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);
