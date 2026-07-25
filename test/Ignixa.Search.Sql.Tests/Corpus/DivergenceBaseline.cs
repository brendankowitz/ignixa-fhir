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
    public const int QueriesOmittingAFilter = 37;

    /// <summary>
    /// Captured queries where each engine applies something the other does not. Guarded alongside
    /// <see cref="QueriesOmittingAFilter"/> because a query that gains a spurious filter flips from
    /// CompilerDoesLess to Divergent, which lowers that count — so without this ceiling the pair of
    /// guards would pass on exactly the regression they exist to catch.
    /// <para>
    /// Raised from 56 to 59 when the corpus began exercising Patient/$everything as a real operation
    /// rather than stripping it to a bare GET /Patient?… (see <see cref="CorpusCompiler"/>). The three
    /// captured $everything queries diverge for a known, benign reason: the honest compartment traversal
    /// reads <c>dbo.ReferenceSearchParam</c> once per compartment-membership parameter (many in real R4),
    /// where the shipping engine's captured SQL read it exactly twice using a windowed/paged form the
    /// compiler does not reproduce. Same semantics, different shape — not a correctness regression.
    /// </para>
    /// </summary>
    public const int DivergingQueries = 59;

    /// <summary>
    /// Captured queries where the compiler applies a filter the shipping engine does not. Guarded
    /// because a query that also starts omitting a required legacy filter flips from CompilerDoesMore
    /// to Divergent, which lowers that count — the same invisible-flip blind spot as
    /// <see cref="DivergingQueries"/>.
    /// </summary>
    public const int QueriesApplyingAnExtraFilter = 14;
}
