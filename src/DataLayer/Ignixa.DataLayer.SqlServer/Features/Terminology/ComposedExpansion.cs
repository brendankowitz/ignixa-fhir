namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// The result of resolving a <c>ValueSet.compose</c>: the codes it designates, and whether anything stopped
/// that resolution from being complete.
/// <para>
/// <see cref="IsPartial"/> is not cosmetic. It is stored on the ValueSet row and reported as
/// <c>expansion.incomplete</c>, and it is also what makes an empty compose still count as expanded — a
/// ValueSet whose every include named an uninstalled CodeSystem exists and is honest about being empty,
/// rather than silently looking like a ValueSet nobody expanded.
/// </para>
/// </summary>
internal sealed record ComposedExpansion(
    IReadOnlyList<ValueSetExpansionRow> Entries,
    bool IsPartial,
    string? PartialReason);
