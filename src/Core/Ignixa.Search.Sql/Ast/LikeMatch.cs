namespace Ignixa.Search.Sql.Ast;

/// <summary>Where a <see cref="Predicate.Like"/>'s value must appear: anywhere, at the start, or at the end.</summary>
public enum LikeMatch { Contains, StartsWith, EndsWith }
