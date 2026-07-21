namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of an IncludeStage's dbo.ReferenceSearchParam row is the seed side (already in the result
/// set, correlated against the match page or a predecessor stage) versus the produced side. A distinct
/// enum from <see cref="ChainDirection"/> because the polarity is inverted — a forward _include's seed is
/// the referencing resource, matching ChainJoin.Reverse's join shape, not Forward's.
/// <para>
/// Forward: seed side is rsp (the referencing resource); produced side is r (the referenced resource,
/// translated via dbo.Resource). Reverse: seed side is r (the referenced resource); produced side is rsp
/// (the referencing resource, selected directly).
/// </para>
/// </summary>
public enum IncludeDirection
{
    Forward,
    Reverse,
}
