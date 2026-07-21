using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The sort-key kinds the compiler can emit joins and value-expressions for. String and Date read from
/// their search-parameter tables; LastUpdated needs no join because ResourceSurrogateId already encodes it.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name -- 'String' mirrors the FHIR sort-parameter type it represents.
public enum SortKeyKind
{
    String,
    Date,
    LastUpdated,
}
#pragma warning restore CA1720

/// <summary>
/// One _sort key. SearchParamId is null only for <see cref="SortKeyKind.LastUpdated"/>.
/// </summary>
public sealed record SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction);

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
/// ISNULL/sentinel substitution applied, so a decoded token compares equal to a live column. All three
/// fields render as bound parameters, never inlined literals, because they are client-controlled input.
/// </summary>
public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,
    SqlParameterRef BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);
