namespace Ignixa.Search.Sql;

/// <summary>
/// A control parameter the server resolved to a value the caller never sent, with why. <see cref="Value"/>
/// is read back off the resolved <see cref="Search.Models.SearchOptions"/> so a changed default shows here;
/// it is display text only — read the typed option itself, never parse this back.
/// </summary>
/// <remarks>
/// "Resolved", not "applied". The values reported today (<c>_count</c>, <c>_total</c>) are advisory: this
/// package emits neither, and both are classified <c>NotApplicable</c> in the plan trace. They tell the caller
/// what the defaults are so it can act on them — by choosing a page size, or by compiling a count plan.
/// </remarks>
public sealed record ImplicitParameter(string Name, string Value, string Reason);
