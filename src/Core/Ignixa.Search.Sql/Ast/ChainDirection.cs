namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of a ChainJoin's dbo.ReferenceSearchParam row is the "known" (InnerMatch-correlated)
/// side versus the "unknown" (dbo.Resource-translated) side. Forward: InnerMatch is the referenced
/// (target) side, translated via dbo.Resource; output is the referencing (source) side, already a
/// surrogate id. Reverse: InnerMatch is the referencing side, correlated directly; output is the
/// referenced side, translated via dbo.Resource. See docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-chain-design.md §2-3.
/// </summary>
public enum ChainDirection
{
    Forward,
    Reverse,
}
