namespace Ignixa.TestScript.Reporting;

/// <summary>
/// One member's outcome within an <c>assertionAnyOfGroup</c> aggregate. Surfaced as diagnostic detail
/// alongside the group's single top-level <see cref="ActionResult"/> — members are never reported as
/// independent top-level results.
/// </summary>
public sealed record AssertionGroupMemberResult(
    string? Description,
    bool Applicable,
    bool Passed,
    string? Message);
