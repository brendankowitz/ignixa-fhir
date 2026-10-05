namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// One expansion entry on its way to <c>dbo.TermValueSetExpansion</c>, from either a pre-computed
/// <c>ValueSet.expansion</c> or a resolved <c>ValueSet.compose</c>. Ordinal is not carried: it is assigned
/// once, after exclusions have been applied, so it always numbers the rows that actually land.
/// </summary>
internal sealed record ValueSetExpansionRow(int SystemId, string Code, string? Display, string? SystemVersion);
