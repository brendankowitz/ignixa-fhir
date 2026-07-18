namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of an IncludeStage's dbo.ReferenceSearchParam row is the "known"/seed side (already in
/// the result set, correlated against cteMatchPage or a predecessor stage) versus the "produced" side
/// (translated via dbo.Resource, or selected directly). A DISTINCT enum from ChainDirection -- the
/// polarity is inverted: forward `_include`'s known side is the referencing resource (already
/// matched), which is the SAME join shape ChainJoin.Reverse emits; `_revinclude`'s known side is the
/// referenced resource, which is ChainJoin.Forward's shape. See
/// docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md §1.2.
/// Forward: known/seed side is rsp (the referencing resource, already a surrogate id); produced side
/// is r (the referenced resource, translated via dbo.Resource).
/// Reverse: known/seed side is r (the referenced resource, translated via dbo.Resource); produced side
/// is rsp (the referencing resource, selected directly).
/// </summary>
public enum IncludeDirection
{
    Forward,
    Reverse,
}
