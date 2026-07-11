namespace Ignixa.TestScript.Expressions;

/// <summary>
/// Parsed form of the <c>http://ignixa.io/testscript/assertionWhenResponseStatus</c> extension: an
/// assertion carrying this makes itself applicable only when the response identified by
/// <paramref name="SourceId"/> — an earlier operation's <c>responseId</c> within the same test —
/// had a status code in <paramref name="Statuses"/>.
/// </summary>
public sealed record ResponseStatusCondition(string SourceId, IReadOnlyList<int> Statuses);
