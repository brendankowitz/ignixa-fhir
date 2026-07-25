namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// How closely the compiler currently tracks the shipping engine on the captured corpus. Distinct from
/// <see cref="DifferentialBaseline"/>, which guards only that a query compiles at all: every captured
/// query already compiles, so the compile count cannot detect a semantic regression. These counters can.
/// </summary>
public static class DivergenceBaseline
{
    /// <summary>Captured queries whose compiled shape reads the same tables with the same filters.</summary>
    public const int MatchingQueries = 75;

    /// <summary>Captured queries where the shipping engine applies a filter the compiler does not.</summary>
    public const int QueriesOmittingAFilter = 40;

    /// <summary>
    /// Captured queries where each engine applies something the other does not. Guarded alongside
    /// <see cref="QueriesOmittingAFilter"/> because a query that gains a spurious filter flips from
    /// CompilerDoesLess to Divergent, which lowers that count — so without this ceiling the pair of
    /// guards would pass on exactly the regression they exist to catch.
    /// </summary>
    public const int DivergingQueries = 56;

    /// <summary>
    /// Captured queries where the compiler applies a filter the shipping engine does not. Guarded
    /// because a query that also starts omitting a required legacy filter flips from CompilerDoesMore
    /// to Divergent, which lowers that count — the same invisible-flip blind spot as
    /// <see cref="DivergingQueries"/>.
    /// </summary>
    public const int QueriesApplyingAnExtraFilter = 14;
}
