using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class SystemLevelSearchSortTests
{
    private static readonly SearchParameterInfo StatusParam =
        new("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

    private static readonly SearchParameterInfo NameParam =
        new("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

    private static SymbolTable Symbols() => new(
        new Dictionary<string, short> { [StatusParam.Url.ToString()] = 202, [NameParam.Url.ToString()] = 77 },
        new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

    private static SearchParameterPredicateExpression StatusFinal() => new(
        StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));

    [Fact]
    public void GivenAnOrdinaryPredicateSortedByAStringKeyWithNoResourceType_WhenLowered_ThenSortComposesNormally()
    {
        // Arrange -- GET /?status=final&_sort=name. A system-level search has no single target resource
        // type, but the sort machinery never needed one: the join correlates on m.T1 rather than on a
        // literal type id, so the guard is gated on the wildcard-compartment case only.
        var symbols = Symbols();

        // Act -- must NOT throw.
        var lowered = Lower.Run(
            StatusFinal(), symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(NameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true });

        // Assert -- a type-less ParamSource plus a working sort join that emits without error.
        var cte = lowered.Plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        cte.ResourceTypeId.ShouldBeNull();
        lowered.Plan.Sort.ShouldNotBeNull();
        lowered.Plan.Sort!.Keys.Count.ShouldBe(1);
        lowered.Plan.Sort.Keys[0].SearchParamId.ShouldBe((short)77);

        var emitted = SqlBuilder.Run(lowered.Plan);
        emitted.Sql.ShouldContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenACrossTypeSortKeyThatOnlySomeTypesCarry_WhenEmittedInTheValuedPhase_ThenTheInnerPrimaryJoinExcludesTheOtherTypesWholesale()
    {
        // Arrange -- GET /?_type=Patient,Observation&status=final&_sort=name. "name" is a Patient
        // parameter; Observation has no row in dbo.StringSearchParam for SearchParamId 77 at all.
        // Keys[0] in the Valued phase is an INNER JOIN correlated on m.T1 with no type scope of its own,
        // so every Observation row fails the join and none of them appear on any Valued page -- the type
        // is excluded wholesale, not interleaved. This is coherent (they surface in MissingPrimary, see
        // the companion test below) but it is the whole-type consequence of INNER, so it is pinned here.
        var symbols = Symbols();

        // Act
        var lowered = Lower.Run(
            StatusFinal(), symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(NameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            SortPhase.Valued, page: null,
            new LowerOptions { SystemLevelSearch = true, ResourceTypes = ["Patient", "Observation"] });
        var emitted = SqlBuilder.Run(lowered.Plan);

        // Assert -- the join is INNER, correlates on m.T1, and carries no ResourceTypeId literal of its
        // own that could have narrowed it to the types that do carry the key.
        emitted.Sql.ShouldContain(
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 77 AND sk0.IsMin = 1");
        emitted.Sql.ShouldNotContain("sk0.ResourceTypeId = 103");
        emitted.Sql.ShouldNotContain("sk0.ResourceTypeId = 104");
    }

    [Fact]
    public void GivenACrossTypeSortKeyThatOnlySomeTypesCarry_WhenEmittedInTheMissingPrimaryPhase_ThenTheNotExistsFilterAdmitsTheOtherTypesWholesale()
    {
        // Arrange -- the same search, second phase. The NOT EXISTS is likewise correlated on m.T1 with no
        // type scope, so every Observation row satisfies it and the entire type lands in this phase.
        var symbols = Symbols();

        // Act
        var lowered = Lower.Run(
            StatusFinal(), symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(NameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            SortPhase.MissingPrimary, page: null,
            new LowerOptions { SystemLevelSearch = true, ResourceTypes = ["Patient", "Observation"] });
        var emitted = SqlBuilder.Run(lowered.Plan);

        // Assert
        emitted.Sql.ShouldNotContain("sk0");
        emitted.Sql.ShouldContain(
            "NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 77)");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithASortKey_WhenLowered_ThenStillThrowsNotSupportedException()
    {
        // Arrange -- the regression guard that keeps the ungate honest: a wildcard compartment search is
        // a DIFFERENT null-resourceType case and must STILL throw. If this breaks, SystemLevelSearch is
        // not discriminating and the relaxation leaked into wildcard compartment too.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [NameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert -- note: no SystemLevelSearch option, so this stays a genuine wildcard
        // compartment search.
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(NameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
                SortPhase.Valued, page: null))
            .Message.ShouldContain("wildcard compartment search");
    }
}
