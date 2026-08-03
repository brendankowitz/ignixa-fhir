// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.Ast;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Proves the global resource-type allow-list (SMART clinical scope) is enforced structurally on every
/// row-producing stage — the match set and every _include/_revinclude/:iterate stage — so a type the caller
/// is not permitted to see cannot be reached, whether searched for directly or navigated to through an
/// include. Unlike an <c>AccessConstraint</c> (a per-type narrowing that leaves unlisted types untouched),
/// an allow-list denies every type it does not name; the wildcard-include and empty-intersection cases are
/// the ones that matter, since either could fail open — the wildcard by producing every type, the empty
/// intersection by emitting no type filter at all.
/// </summary>
public class AllowedResourceTypesTests
{
    private const short ObservationTypeId = 104;
    private const short PatientTypeId = 103;
    private const short DeviceTypeId = 105;
    private const short SubjectParamId = 230;
    private const short StatusParamId = 220;

    private sealed record Fixture(
        SymbolTable Symbols,
        SearchParameterInfo SubjectParam,
        SearchParameterInfo StatusParam);

    /// <summary>
    /// A symbol table with Observation=104, Patient=103, Device=105, an Observation.subject reference
    /// parameter (id 230) whose targets are Patient and Device, and an Observation.status token parameter
    /// (id 220).
    /// </summary>
    private static Fixture Arrange()
    {
        var subjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            targetResourceTypes: ["Patient", "Device"]);
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

        var symbols = new SymbolTable(
            new Dictionary<string, short>
            {
                [subjectParam.Url!.ToString()] = SubjectParamId,
                [statusParam.Url!.ToString()] = StatusParamId,
            },
            new Dictionary<string, short> { ["Observation"] = ObservationTypeId, ["Patient"] = PatientTypeId, ["Device"] = DeviceTypeId });

        return new Fixture(symbols, subjectParam, statusParam);
    }

    private static QueryPlan Lowered(Fixture f, string? targetResourceType, LowerOptions options, IReadOnlyList<IncludeExpression>? revIncludes = null, IReadOnlyList<IncludeExpression>? includes = null)
        => LowerHarness.Run(
            expression: null, f.Symbols, targetResourceType, includes: includes ?? [], revIncludes: revIncludes ?? [], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, options).Plan;

    [Fact]
    public void GivenASingleTypeMatch_WhenItsTypeIsNotAllowed_ThenTheMatchIntersectsToNoRows()
    {
        // Arrange -- an Observation search under an allow-list of only Patient. The caller may not see any
        // Observation, so the match must produce nothing.
        var f = Arrange();

        // Act
        var plan = Lowered(f, "Observation", new LowerOptions { AllowedResourceTypes = ["Patient"] });
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the match root is the Observation source intersected with the allowed base set, and that
        // base set is Patient only. Observation ∩ {Patient rows} is empty, so the plan returns no rows.
        // AST over SQL because the CTE numbers shift but the relationship does not.
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        ctes[root.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>().ResourceTypeId.ShouldBe(ObservationTypeId);
        ctes[root.Right.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>().ResourceTypeIds.ShouldBe(new short[] { PatientTypeId });
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAMultiTypeMatch_WhenSomeTypesAreNotAllowed_ThenTheMatchNarrowsToTheIntersection()
    {
        // Arrange -- GET /?_type=Observation,Patient,Device under an allow-list of Patient,Observation. The
        // match must keep Observation and Patient and drop Device.
        var f = Arrange();

        // Act
        var plan = LowerHarness.Run(
            expression: null, f.Symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            new LowerOptions { ResourceTypes = ["Observation", "Patient", "Device"], AllowedResourceTypes = ["Patient", "Observation"] }).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the searched base set (all three requested types) intersected with the allowed base set
        // (Patient, Observation). Device is present on the left but not the right, so it cannot survive the
        // intersection. Asserting the shape, not just presence, so neutralising RestrictMatch (leaving the
        // root as the bare three-type scan) fails here.
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        ctes[root.Left.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>().ResourceTypeIds.ShouldBe(new short[] { ObservationTypeId, PatientTypeId, DeviceTypeId });
        ctes[root.Right.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>().ResourceTypeIds.ShouldBe(new short[] { PatientTypeId, ObservationTypeId });
        sql.ShouldContain($"ResourceTypeId IN ({PatientTypeId}, {ObservationTypeId})");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAForwardInclude_WhenOneTargetTypeIsNotAllowed_ThenTheStageEmitsATypeFilterExcludingIt()
    {
        // Arrange -- Observation?_include=Observation:subject, whose subject targets Patient and Device.
        // Under an allow-list of Observation,Patient the include may return Patient but not Device.
        var f = Arrange();
        var include = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", targetResourceType: null, referencedTypes: null, wildCard: false, reversed: false, iterate: false);

        // Act
        var plan = Lowered(f, "Observation", new LowerOptions { AllowedResourceTypes = ["Observation", "Patient"] }, includes: [include]);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the forward stage's output types (Patient, Device) are intersected with the allow-list
        // down to Patient. The emitter renders it as the legacy "outputTypeColumn IN (...)" filter on the
        // r (produced-row) alias.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds.ShouldBe(new short[] { PatientTypeId });
        sql.ShouldContain($"r.ResourceTypeId = {PatientTypeId}");
        sql.ShouldNotContain($"r.ResourceTypeId = {DeviceTypeId}");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAReverseInclude_WhenItsProducedTypeIsNotAllowed_ThenTheStageEmitsATypeFilterExcludingIt()
    {
        // Arrange -- Patient?_revinclude=Observation:subject under an allow-list of Patient,Device. The
        // revinclude produces Observation, which is not permitted, so the stage must return nothing.
        var f = Arrange();
        var revinclude = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false);

        // Act
        var plan = Lowered(f, "Patient", new LowerOptions { AllowedResourceTypes = ["Patient", "Device"] }, revIncludes: [revinclude]);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- Observation ∩ {Patient, Device} is empty, so the stage's output types collapse to the
        // unmatchable sentinel (-1) and the emitter renders "rsp.ResourceTypeId = -1" on the reverse
        // (produced-row) alias -- a filter that matches no row rather than no filter at all.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds.ShouldBe(new short[] { SymbolTable.UnmatchableResourceTypeId });
        sql.ShouldContain("rsp.ResourceTypeId = -1");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAWildcardInclude_WhenLowered_ThenTheNullOutputTypesBecomeTheAllowList()
    {
        // Arrange -- Patient?_revinclude=* under an allow-list of Patient,Observation. A wildcard stage has
        // null output types (it can otherwise produce every type), the case most likely to fail open. The
        // allow-list must become its output type filter.
        var f = Arrange();
        var wildcard = new IncludeExpression(["*"], referenceSearchParameter: null, "*", "Patient", referencedTypes: ["Observation"], wildCard: true, reversed: true, iterate: false);

        // Act
        var plan = Lowered(f, "Patient", new LowerOptions { AllowedResourceTypes = ["Patient", "Observation"] }, revIncludes: [wildcard]);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the previously-null output types are replaced by the allow-list ids, so the wildcard can
        // only ever return permitted types.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds.ShouldBe(new short[] { PatientTypeId, ObservationTypeId });
        sql.ShouldContain($"rsp.ResourceTypeId = {PatientTypeId}");
        sql.ShouldContain($"rsp.ResourceTypeId = {ObservationTypeId}");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAnIterateInclude_WhenItsProducedTypeIsNotAllowed_ThenTheIterateStageIsRestrictedToo()
    {
        // Arrange -- same shape as the reverse-include test but the stage is :iterate. An :iterate stage is
        // the same IncludeStage record and must get the same enforcement.
        var f = Arrange();
        var iterate = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: true);

        // Act
        var plan = Lowered(f, "Patient", new LowerOptions { AllowedResourceTypes = ["Patient"] }, revIncludes: [iterate]);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the iterate stage produces Observation, which the Patient-only allow-list denies, so its
        // output types collapse to the unmatchable sentinel exactly as a non-iterate stage would.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].Iterate.ShouldBeTrue();
        plan.Includes![0].OutputTypeIds.ShouldBe(new short[] { SymbolTable.UnmatchableResourceTypeId });
        sql.ShouldContain("rsp.ResourceTypeId = -1");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenAnIncludeWhoseIntersectionIsEmpty_WhenLowered_ThenItEmitsAnUnmatchableFilterRatherThanAnUnfilteredStage()
    {
        // Arrange -- the fail-open hazard this feature turns on. A forward include producing Patient/Device
        // under an allow-list of Observation intersects to nothing. The emitter renders the output-type
        // filter ONLY when the list is { Count: > 0 }, so an EMPTY list would emit no filter and the stage
        // would return every type it can reach -- fail-open. The lowering must instead leave a single
        // unmatchable sentinel so a filter is still emitted.
        var f = Arrange();
        var include = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", targetResourceType: null, referencedTypes: null, wildCard: false, reversed: false, iterate: false);

        // Act
        var plan = Lowered(f, "Observation", new LowerOptions { AllowedResourceTypes = ["Observation"] }, includes: [include]);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the output types are NOT an empty list (which would fail open) but exactly [-1], and the
        // emitted SQL carries a type filter that matches no row rather than omitting one entirely.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds!.ShouldNotBeEmpty();
        plan.Includes![0].OutputTypeIds.ShouldBe(new short[] { SymbolTable.UnmatchableResourceTypeId });
        sql.ShouldContain("r.ResourceTypeId = -1");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenASystemLevelSearchWhoseMatchIsAChain_WhenTheChainOutputTypeIsNotAllowed_ThenTheMatchIsStillRestricted()
    {
        // Arrange -- GET /?subject:Patient.status=final under an allow-list of Patient only. A chain under a
        // null target type only became reachable when the leaf-dispatch guard was removed, so RestrictMatch
        // over a ChainJoin-derived match is a newly live authorization path with no coverage. The chain emits
        // Observation, which the caller may not see.
        var f = Arrange();
        var chain = new ChainedExpression(
            resourceTypes: ["Observation"],
            referenceSearchParameter: f.SubjectParam,
            targetResourceTypes: ["Patient"],
            reversed: false,
            expression: new SearchParameterExpression(
                f.StatusParam,
                new SearchParameterPredicateExpression(f.StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));

        // Act
        var plan = LowerHarness.Run(
            chain, f.Symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            new LowerOptions { SystemLevelSearch = true, AllowedResourceTypes = ["Patient"] }).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the match root is the chain intersected with the allowed base set, so the Observation
        // rows the chain produces cannot survive. Neutralising RestrictMatch would leave the root as the
        // bare ChainJoin and fail here.
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        ctes[root.Left.Index].ShouldBeOfType<CteDefinition.ChainJoin>().OutputResourceTypeIds.ShouldBe(new[] { ObservationTypeId });
        ctes[root.Right.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>().ResourceTypeIds.ShouldBe(new[] { PatientTypeId });
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenNoAllowList_WhenLowered_ThenThePlanIsIdenticalToOneLoweredWithoutTheParameter()
    {
        // Arrange -- a match plus a forward include, so both enforcement paths are exercised. Null and empty
        // must both be inert: an unrestricted plan is byte-identical to one compiled before the allow-list
        // existed, the same guarantee AccessConstraintApplier.IsEmpty gives.
        var f = Arrange();
        var include = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", targetResourceType: null, referencedTypes: null, wildCard: false, reversed: false, iterate: false);

        string Build(IReadOnlyList<string>? allowed) => SqlBuilder.Run(LowerHarness.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [include], revIncludes: [], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { AllowedResourceTypes = allowed }).Plan).Sql;

        // Act + Assert
        Build(null).ShouldBe(Build(Array.Empty<string>()));
    }
}
