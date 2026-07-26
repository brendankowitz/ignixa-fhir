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
    /// the patient. So the two engines ask the database for different things — opposite graph direction,
    /// plus the paging/hydration machinery — not a windowed-vs-unwound batching of the same membership
    /// reads. (The capture's opaque SearchParamIds can't be name-mapped, so exactly which two patient
    /// reference parameters the engine expanded isn't verifiable; the outbound direction is.)
    /// <para>
    /// One clause of that account is now obsolete and has been struck: the compiler no longer "emits no
    /// referenced-resource union at all". <c>StructuralContext.LowerPatientEverything</c> emits a
    /// ReferencedTypeExpansion — the outbound Practitioner/Organization/Location/Medication follow, seeded
    /// from the filtered compartment set — so the compiler and the engine now agree on that half. The
    /// remaining, unclosed divergence is the inbound compartment traversal the engine's capture does not
    /// contain, and the paging machinery. The count did not move: a verdict is categorical, all three
    /// entries were already Divergent, and closing one contributing difference out of several cannot flip
    /// a query out of that bucket. Per the convention below, the changed reason is recorded here rather
    /// than the count being adjusted.
    /// <para>
    /// That account's "opposite graph direction" framing was itself wrong, not just the "no
    /// referenced-resource union" clause struck above. The captured SQL is not the whole $everything
    /// operation — it is phase 1 of Microsoft fhir-server's own <em>phased</em> $everything, and the
    /// capture never followed its continuation token to phases 2–4. The evidence is in the capture itself
    /// (<c>legacy-sql-corpus.json</c>, around lines 3072/3135/3198): the <c>@FilteredData</c> table
    /// variable, <c>IsMatch</c>/<c>IsPartial</c> columns, the <c>TOP (@p) = 1001</c> include ceiling, and
    /// the <c>Row &lt; @p</c> window are fhir-server's <c>_include</c> machinery, not bespoke $everything
    /// SQL. The seed Patient is the sole match (<c>ResourceId = @p0</c>, <c>IsMatch = 1</c>), and the two
    /// expansions marked <c>IsMatch = 0</c> follow exactly two outbound reference parameters
    /// (SearchParamIds 1012 and 1017) — almost certainly <c>Patient.general-practitioner</c> and
    /// <c>Patient.organization</c>, finally supplying the name-mapping the paragraph above said was
    /// unverifiable. Microsoft documents the phasing: phase 1 returns the Patient plus its
    /// generalPractitioner and managingOrganization; phases 2–3 return the patient compartment behind that
    /// continuation token; phase 4 returns devices referencing the patient. So the capture reads outbound
    /// only because phase 1 <em>is</em> outbound-only — not because the engine and the compiler disagree
    /// on graph direction. The FHIR spec (identical wording in STU3, R4, and R5) requires the compartment
    /// <em>and</em> resources referenced from it, and this repo's own legacy EF generator performs both
    /// directions in a single query — the compiler is a transliteration of it, not a divergent design.
    /// Combined with the seed-union fix in <c>StructuralContext.LowerPatientEverything</c>
    /// (<c>Union(patientItselfRef, filteredCompartmentRef)</c> now feeds the ReferencedTypeExpansion,
    /// closing the shared under-return bug where the patient's own generalPractitioner/managingOrganization
    /// were missed whenever no compartment member happened to reference them too), the sole remaining
    /// reason these three entries are Divergent is the paging model — phased continuation token versus a
    /// single windowed query — which is a future phase's subject, not a semantics gap. The count still does
    /// not move, for the same categorical reason as above.
    /// </para>
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
