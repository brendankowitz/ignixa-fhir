namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of a ChainJoin's dbo.ReferenceSearchParam row is the known (InnerMatch-correlated) side
/// versus the side translated through dbo.Resource. Forward: InnerMatch is the referenced (target) side,
/// translated via dbo.Resource, and the output is the referencing (source) side. Reverse: InnerMatch is
/// the referencing side, correlated directly, and the output is the referenced side.
/// </summary>
public enum ChainDirection
{
    Forward,
    Reverse,
}
