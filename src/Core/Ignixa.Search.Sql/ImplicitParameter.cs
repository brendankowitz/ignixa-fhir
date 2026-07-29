namespace Ignixa.Search.Sql;

/// <summary>
/// A control parameter the server resolved to a value the caller never sent, with why. <see cref="Value"/>
/// is read back off the resolved <see cref="Search.Models.SearchOptions"/> so a changed default shows here;
/// it is display text only — read the typed option itself, never parse this back.
/// </summary>
public sealed record ImplicitParameter(string Name, string Value, string Reason);
