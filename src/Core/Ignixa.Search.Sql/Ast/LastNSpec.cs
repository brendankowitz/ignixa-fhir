namespace Ignixa.Search.Sql.Ast;

/// <summary>Resolved coordinates for the Observation <c>$lastn</c> terminal result shape.</summary>
public sealed record LastNSpec(
    short ResourceTypeId,
    short CodeSearchParamId,
    short EffectiveDateSearchParamId,
    int Maximum);
