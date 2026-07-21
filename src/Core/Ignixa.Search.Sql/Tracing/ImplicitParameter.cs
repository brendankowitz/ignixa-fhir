namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// A control parameter the server resolved to a value the caller never sent, with why it did.
/// </summary>
/// <remarks>
/// Nothing in a search response distinguishes "you asked for 10 results" from "you asked for nothing and
/// got the server's 10", nor explains a total the caller never requested. Both are decisions the request
/// text does not record, so they can only be surfaced from the resolved
/// <see cref="Search.Models.SearchOptions"/>. <see cref="Value"/> is always read back off those resolved
/// options rather than restated from a constant, so a changed default shows up here instead of drifting.
/// It is display text only — never parse it back into a typed option, read the option itself.
/// </remarks>
public sealed record ImplicitParameter(string Name, string Value, string Reason);
