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
    /// rather than stripping it to a bare GET /Patient?… (see <see cref="CorpusCompiler"/>). All three
    /// captured $everything queries land as Divergent — but <em>not</em> for the shape-only reason first
    /// recorded here ("the compiler reads <c>dbo.ReferenceSearchParam</c> once per membership parameter
    /// where the engine read it twice in a windowed/paged form; same semantics, different shape"). Reading
    /// the captured SQL shows that reason was wrong: the divergence is semantic, and in opposite directions.
    /// The shipping engine's paged $everything reads <c>dbo.ReferenceSearchParam</c> exactly twice, and both
    /// reads follow the seed patient's <em>outbound</em> references — <c>refSource.ResourceTypeId IN
    /// (Patient)</c>, joined through <c>dbo.Resource</c> to materialize each referenced target — i.e.
    /// referenced-resource inclusion (the resources the patient points to). The compiler instead emits the
    /// <em>inbound</em> compartment-membership traversal: one <c>dbo.ReferenceSearchParam</c> read per
    /// Patient-compartment membership parameter (many in real R4), matching resources that point <em>at</em>
    /// the patient, and it emits no referenced-resource union at all. So the two engines ask the database for
    /// different things — opposite graph direction, plus the compiler's omission of referenced-resource
    /// inclusion and of the paging/hydration machinery — not a windowed-vs-unwound batching of the same
    /// membership reads. (The capture's opaque SearchParamIds can't be name-mapped, so exactly which two
    /// patient reference parameters the engine expanded isn't verifiable; the outbound direction, and the
    /// compiler's omission of the referenced-resource union, are.) The count did not move for this
    /// correction — only the recorded cause. Per the convention below, the reason is recorded here rather
    /// than the count being suppressed.
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
