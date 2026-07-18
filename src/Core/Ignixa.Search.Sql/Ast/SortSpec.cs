using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which FHIR sort-key kinds this compiler can emit joins/value-expressions for. String and Date are
/// the only search-parameter-table kinds fhir-server's own SQL sort path supports; LastUpdated is a
/// resource-column kind needing no join at all (ResourceSurrogateId already encodes it, per the
/// compiler's existing ResourceColumnLoweringRule precedent).
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
/// One _sort key. SearchParamId is null only for Kind == LastUpdated. Reuses Ignixa.Search.Expressions.SortOrder
/// (Ascending/Descending) directly rather than a new enum -- no polarity-inversion risk exists here the
/// way ChainDirection/IncludeDirection's own distinct-enum precedent was protecting against.
/// </summary>
public sealed record SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction);

/// <summary>
/// Which two-phase missing-value segment this plan computes -- Valued (Keys[0]'s join is INNER,
/// gating on presence) or MissingPrimary (Keys[0] is excluded from the join list entirely, replaced by
/// a NOT EXISTS clause). Only Keys[0] (the primary key) has a phase; every other key is always a
/// LEFT JOIN tie-breaker in either phase. The phase is a CALLER input -- Lower does not compute it by
/// inspecting the query, matching fhir-server's own executor-driven phase-transition model. See
/// docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-sort-design.md §1.2/§3.2.
/// </summary>
public enum SortPhase
{
    Valued,
    MissingPrimary,
}

/// <summary>
/// A compiled _sort, capped at 3 keys (Global Constraints) this phase. Keys[0] is the primary key,
/// whose presence/absence Phase segments; Keys[1..] are always ordinary LEFT-JOIN tie-breakers.
/// </summary>
public sealed record SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase);

/// <summary>
/// The keyset boundary decoded from a continuation token by the caller -- null PageSpec means "first
/// page." Boundary carries one value per ACTIVE key for the current SortSpec.Phase: Keys.Count values
/// in SortPhase.Valued, Keys.Count-1 in SortPhase.MissingPrimary (Keys[0] excluded, since the primary
/// key has no value in that phase by construction). Values are POST-sentinel-substitution (§3.3) --
/// the caller is responsible for applying the same ISNULL/sentinel logic to a decoded token value that
/// Emit applies to a live column, so the two compare correctly. All three fields render as bound
/// SqlParameterRefs, never inlined literals -- they are client-controlled input.
/// </summary>
public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,
    SqlParameterRef BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);
