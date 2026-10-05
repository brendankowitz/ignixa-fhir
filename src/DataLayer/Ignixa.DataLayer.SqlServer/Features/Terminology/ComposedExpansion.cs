namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// The result of resolving a <c>ValueSet.compose</c>: the codes it designates, and whether anything stopped
/// that resolution from being complete.
/// <para>
/// <see cref="IsPartial"/> is not cosmetic. It is stored on the ValueSet row as
/// <c>TermValueSet.IsPartialExpansion</c>, read back into <c>ExpandResult.Incomplete</c> and reported to
/// the client as <c>expansion.incomplete</c> — a ValueSet whose every include named an uninstalled
/// CodeSystem is empty because this server could not resolve it, not because it designates nothing, and a
/// caller cannot tell those apart from the codes alone.
/// </para>
/// <para>
/// It no longer decides whether the ValueSet counts as expanded. <c>dbo.ImportTermValueSet</c> marks every
/// import expanded, so a correctly empty expansion — every included code excluded again — stays visible to
/// <c>$expand</c> and <c>$validate-code</c> without having to claim it was partial.
/// </para>
/// </summary>
internal sealed record ComposedExpansion(
    IReadOnlyList<ValueSetExpansionRow> Entries,
    bool IsPartial,
    string? PartialReason);
