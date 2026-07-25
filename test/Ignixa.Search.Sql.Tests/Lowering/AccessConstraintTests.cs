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
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Proves access constraints are enforced structurally on every row-producing site — the match set, each
/// _include/_revinclude/:iterate stage, and each chain target — not only on the top-level match. The
/// revinclude, chain, and iterate tests are the ones that matter: an expression-rewriting approach would
/// narrow the match set yet leave those reachability paths unguarded.
/// </summary>
public class AccessConstraintTests
{
    private const short ObservationTypeId = 104;
    private const short PatientTypeId = 103;
    private const short StatusParamId = 220;
    private const short SubjectParamId = 230;
    private const short CategoryParamId = 240;

    private sealed record Fixture(
        SymbolTable Symbols,
        SearchParameterInfo StatusParam,
        SearchParameterInfo SubjectParam,
        SearchParameterInfo CategoryParam,
        AccessConstraint ObservationConstraint);

    /// <summary>
    /// A symbol table with Observation=104, Patient=103, an Observation.status token parameter (id 220), an
    /// Observation.subject reference parameter (id 230), and an Observation.category token parameter (id
    /// 240); plus an AccessConstraint("Observation", status eq final) built from that status parameter.
    /// </summary>
    private static Fixture Arrange()
    {
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var categoryParam = new SearchParameterInfo("category", "category", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-category"));

        var symbols = new SymbolTable(
            new Dictionary<string, short>
            {
                [statusParam.Url!.ToString()] = StatusParamId,
                [subjectParam.Url!.ToString()] = SubjectParamId,
                [categoryParam.Url!.ToString()] = CategoryParamId,
            },
            new Dictionary<string, short> { ["Observation"] = ObservationTypeId, ["Patient"] = PatientTypeId });

        var constraint = new AccessConstraint("Observation", TokenPredicate(statusParam, "final"));
        return new Fixture(symbols, statusParam, subjectParam, categoryParam, constraint);
    }

    /// <summary>A wrapped token predicate ("&lt;param&gt; eq &lt;code&gt;"), the shape a real bound leaf takes.</summary>
    private static Expression TokenPredicate(SearchParameterInfo parameter, string code)
        => new SearchParameterExpression(
            parameter,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null)));

    [Fact]
    public void GivenAConstrainedType_WhenSearchedDirectly_ThenTheConstraintNarrowsTheMatchSet()
    {
        // Arrange
        var f = Arrange();

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the status=final constraint (SearchParamId 220) is intersected into the match set.
        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAConstrainedType_WhenReachedOnlyThroughAnInclude_ThenTheIncludeStageIsStillConstrained()
    {
        // Arrange -- a Patient search whose only path to an Observation is _revinclude=Observation:subject.
        // The match set is Patient; the Observation status=final constraint must attach to the revinclude
        // stage, not to the match set. This is the test the whole task exists to make pass.
        var f = Arrange();
        var revinclude = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false);

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [revinclude], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- 220 appears only if the include stage carries the constraint; the match set is Patient
        // and never mentions status.
        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAConstrainedType_WhenReachedAsAChainTarget_ThenTheChainTargetIsConstrained()
    {
        // Arrange -- Patient?_has:Observation:subject:category=vital-signs. The inner category predicate
        // (SearchParamId 240) is evaluated against Observation; the Observation status=final constraint
        // (SearchParamId 220) must also apply to that chain-target scope.
        var f = Arrange();
        var chain = new ChainedExpression(
            resourceTypes: ["Observation"],
            referenceSearchParameter: f.SubjectParam,
            targetResourceTypes: ["Patient"],
            reversed: true,
            expression: TokenPredicate(f.CategoryParam, "vital-signs"));

        // Act
        var plan = Lower.Run(
            chain, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- both the inner predicate and the constraint are present on the chain target.
        sql.ShouldContain("SearchParamId = 240");
        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAConstrainedType_WhenReachedThroughAnIterateStage_ThenTheIterateStageIsConstrained()
    {
        // Arrange -- same shape as the include test but the stage is :iterate. Iterate stages are the same
        // IncludeStage record and must get the same treatment.
        var f = Arrange();
        var iterate = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: true);

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [iterate], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].Iterate.ShouldBeTrue();
        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAWildcardIncludeWithNullOutputTypes_WhenAConstrainedTypeCouldBeReached_ThenTheConstraintStillApplies()
    {
        // Arrange -- a wildcard _revinclude=* whose produced types are unknown at compile time
        // (OutputTypeIds is null). A constraint must still be enforced: failing open here is a security
        // hole, so every constrained type is guarded conservatively.
        var f = Arrange();
        var wildcard = new IncludeExpression(["*"], referenceSearchParameter: null, "*", "Patient", referencedTypes: ["Observation"], wildCard: true, reversed: true, iterate: false);

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [wildcard], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the wildcard stage carries the Observation constraint even though its output types are
        // not enumerable.
        plan.Includes.ShouldNotBeNull();
        plan.Includes![0].OutputTypeIds.ShouldBeNull();
        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAMultiTypeMatch_WhenOneTypeIsConstrained_ThenThatTypeIsNarrowedAndOthersAreUntouched()
    {
        // Arrange -- a system-wide _type=Observation,Patient search. Only Observation is constrained; the
        // guard must narrow Observation rows without dropping Patient rows.
        var f = Arrange();

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint],
            resourceTypes: ["Observation", "Patient"]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the constraint is present, and the plan remains valid T-SQL.
        sql.ShouldContain("SearchParamId = 220");
        SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenNoConstraints_WhenLowered_ThenThePlanIsIdenticalToOneLoweredWithoutTheParameter()
    {
        // Arrange
        var f = Arrange();

        QueryPlanSql Build(IReadOnlyList<AccessConstraint>? constraints) => new(SqlBuilder.Run(Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: constraints).Plan).Sql);

        // Act + Assert -- null and empty must both be inert.
        Build(null).Sql.ShouldBe(Build(Array.Empty<AccessConstraint>()).Sql);
    }

    [Fact]
    public void GivenDuplicateConstraintsForTheSameType_WhenLowered_ThenItThrowsRatherThanSilentlyKeepingOne()
    {
        // Arrange -- two constraints for Observation is a caller error (claim translation should combine
        // them into one predicate). Silently keeping the first would be the dangerous outcome; we throw.
        var f = Arrange();
        var duplicate = new AccessConstraint("Observation", TokenPredicate(f.CategoryParam, "vital-signs"));

        // Act + Assert
        var ex = Should.Throw<ArgumentException>(() => Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint, duplicate]));
        ex.Message.ShouldContain("Observation");
    }

    [Fact]
    public void GivenAConstraintForATypeNotInTheQuery_WhenLowered_ThenItIsInertRatherThanErroring()
    {
        // Arrange -- a Device constraint on an Observation search. Device produces no rows here, so the
        // constraint binds nowhere; it is inert, not silently dropped. The Observation constraint still
        // applies. The Device parameter is deliberately absent from the symbol table: proof it is never
        // lowered (doing so would throw KeyNotFoundException).
        var f = Arrange();
        var deviceParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Device-status"));
        var deviceConstraint = new AccessConstraint("Device", TokenPredicate(deviceParam, "active"));

        // Act
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.ObservationConstraint, deviceConstraint]).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- Observation constraint enforced; no throw despite the unresolved Device parameter.
        sql.ShouldContain("SearchParamId = 220");
    }

    private sealed record QueryPlanSql(string Sql);
}
