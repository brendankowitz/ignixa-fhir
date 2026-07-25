namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>How the compiler's SQL relates to the shipping engine's for the same query.</summary>
public enum ShapeVerdict
{
    /// <summary>The compiler could not produce SQL at all -- a feature gap.</summary>
    NotCompiled,

    /// <summary>Same tables, same semantic filters. Any remaining difference is encoding.</summary>
    Match,

    /// <summary>The compiler reads or filters strictly less than the shipping engine does.</summary>
    CompilerDoesLess,

    /// <summary>The compiler reads or filters strictly more than the shipping engine does.</summary>
    CompilerDoesMore,

    /// <summary>Each side does something the other does not.</summary>
    Divergent,
}
