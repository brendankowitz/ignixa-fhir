namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of an IncludeStage's dbo.ReferenceSearchParam row is the seed (already in the result set)
/// versus the produced side. Distinct from <see cref="ChainDirection"/> because the polarity is inverted —
/// a forward _include's seed is the referencing resource (rsp), matching ChainJoin.Reverse, and its produced
/// side is the referenced resource (r); Reverse swaps them.
/// </summary>
public enum IncludeDirection
{
    Forward,
    Reverse,
}
