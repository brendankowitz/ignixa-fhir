// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ProjectionFixture;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>fhirpath</c>: one FHIRPath value over a searchset Bundle of every match, cited by the resources it was read or computed
/// from. New capability: at ab2447f3 a search returned only resources and a count.
/// </summary>
public class ExpressionSearchTests
{
    private const string Patient = "Patient/pa";

    private const string A1c = "code=http://loinc.org|4548-4";

    private static SearchRequest Labs(string expression, string query = A1c) => new("Observation", query, Compartment: Patient, FhirPath: expression);

    [Fact]
    public void GivenAnAggregate_WhenEvaluated_ThenTheValueIsCitedByTheResourceItWasReadFrom()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs("entry.resource.value.ofType(Quantity).value.max()"));

        SearchFacts facts = Facts(envelope);
        facts.Value.ShouldNotBeNull().ToJsonString().ShouldBe("[7.4]");
        facts.ValueFrom.ShouldBe(ExpressionSearch.FromResultElements);
        facts.FhirPath.ShouldBe("entry.resource.value.ofType(Quantity).value.max()");
        facts.Total.ShouldBe(3);
        facts.Returned.ShouldBe(0);
        facts.Resources.ShouldBeNull();
        Cited(envelope).ShouldBe(["Observation/a2"]);
    }

    [Fact]
    public void GivenACount_WhenEvaluated_ThenTheValueIsCitedByEveryMatch()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs("entry.count()"));

        Facts(envelope).Value.ShouldNotBeNull().ToJsonString().ShouldBe("[3]");
        Facts(envelope).ValueFrom.ShouldBe(ExpressionSearch.FromMatches);
        Cited(envelope).ShouldBe(["Observation/a1", "Observation/a2", "Observation/a3"]);
    }

    [Fact]
    public void GivenACountOverManyMatches_WhenEvaluated_ThenEvidenceCitesTheFirstMatchesAndOmittedCountsTheRest()
    {
        using TestFolder folder = new TestFolder().Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, 40).Select(i =>
            $$$"""{"resourceType":"Observation","id":"o{{{i:00}}}","status":"final","code":{"text":"x"}}""")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", FhirPath: "entry.count()"));

        Facts(envelope).Value.ShouldNotBeNull().ToJsonString().ShouldBe("[40]");
        envelope.Evidence.Count.ShouldBe(ExpressionSearch.MatchEvidenceLimit);
        envelope.Omitted.ShouldBe([new OmittedItem($"{40 - ExpressionSearch.MatchEvidenceLimit} more resources the value was computed from are not listed in evidence (it lists the first {ExpressionSearch.MatchEvidenceLimit}); a search with the same query lists them", "limit")]);
    }

    [Fact]
    public void GivenSortedMatches_WhenTheFirstEntryIsTaken_ThenItIsTheLatestValue()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs("entry.resource.first().valueQuantity", $"{A1c}&_sort=-date"));

        Facts(envelope).Value.ShouldNotBeNull().ToJsonString().ShouldBe("""[{"value":7.4,"unit":"%"}]""");
        Cited(envelope).ShouldBe(["Observation/a2"]);
    }

    [Fact]
    public void GivenResolveInTheExpression_WhenEvaluated_ThenTheResolvedResourcesAreCitedAfterTheMatches()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(
            Open(folder),
            new SearchRequest("MedicationRequest", "status=active", Compartment: Patient, FhirPath: "entry.resource.requester.resolve().name.family"));

        Facts(envelope).ValueFrom.ShouldBe(ExpressionSearch.FromMatches);
        Facts(envelope).Value.ShouldNotBeNull().ToJsonString().ShouldBe("""["Okafor","Marquardt"]""");
        Cited(envelope).ShouldBe(["MedicationRequest/m2", "MedicationRequest/m3", "MedicationRequest/m4", "Practitioner/pr2", "Practitioner/pr1"]);
        envelope.Issues.ShouldContain(issue => issue.Code == "resolve-target-absent");
    }

    [Fact]
    public void GivenAnExpressionYieldingNothing_WhenEvaluated_ThenTheValueIsAnEmptyArray()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs("entry.resource.where(valueQuantity.value > 100)"));

        Facts(envelope).Value.ShouldNotBeNull().ShouldBeEmpty();
        Facts(envelope).ValueFrom.ShouldBe(ExpressionSearch.FromMatches);
    }

    [Theory]
    [InlineData("entry.resource.(", "is not valid FHIRPath")]
    [InlineData("entry.notAFunction()", "could not be evaluated on the Bundle of 3 matches")]
    public void GivenAnInvalidExpression_WhenEvaluated_ThenTheCallFailsWithInvalidParameterQuotingIt(string expression, string problem)
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Labs(expression));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("invalid-parameter");
        error.Diagnostics.ShouldContain($"'{expression}'");
        error.Diagnostics.ShouldContain(problem);
    }

    [Theory]
    [InlineData(3, 0, null)]
    [InlineData(null, 1, null)]
    [InlineData(null, 0, "_count=2")]
    public void GivenPaging_WhenEvaluated_ThenTheCallFailsBecauseTheExpressionSeesEveryMatch(int? count, int offset, string? countParameter)
    {
        using TestFolder folder = Synthea();
        SearchRequest request = Labs("entry.count()", countParameter is null ? A1c : $"{A1c}&{countParameter}") with { Count = count, Offset = offset };

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), request);

        Error(envelope).Diagnostics.ShouldContain("do not apply");
    }

    [Fact]
    public void GivenAValueLargerThanTheBudget_WhenEvaluated_ThenItIsCutWithinTheBudgetAndOmittedSaysHowToNarrowIt()
    {
        using TestFolder folder = new TestFolder().Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, 60).Select(i =>
            $$"""{"resourceType":"Observation","id":"o{{i:00}}","status":"final","code":{"text":"x"},"note":[{"text":"{{new string('n', 400)}}"}]}""")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", FhirPath: "entry.resource.note.text"));

        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        int shown = Facts(envelope).Value.ShouldNotBeNull().Count;
        shown.ShouldBeInRange(1, 59);
        envelope.Omitted.ShouldContain(item => item.What.StartsWith($"{60 - shown} more items of the value (offset={shown})", StringComparison.Ordinal));
    }
}
