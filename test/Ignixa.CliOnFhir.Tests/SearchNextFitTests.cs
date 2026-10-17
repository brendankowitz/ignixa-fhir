// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The calls in <c>next</c> repeat the query whole, so a query too long to repeat beside the page leaves them out instead of
/// breaking the size cap. Regression (Task 12b, found when the envelope gained <c>asOf</c>): at aaa08fda a long lenient query with
/// more matches than one page reserved the long calls, read no resource, and sent a response of hundreds of kilobytes.
/// </summary>
public class SearchNextFitTests
{
    private static TestFolder ManyObservations() => new TestFolder().Write("obs.ndjson", string.Join('\n', Enumerable.Range(1, 30).Select(index =>
        $$$"""{"resourceType":"Observation","id":"o{{{index}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"valueQuantity":{"value":{{{index}}},"unit":"/min"}}""")));

    private static string Unknown(int count) => string.Join('&', Enumerable.Range(1, count).Select(index => $"unknown-parameter-{index:0000}=some-value-{index}"));

    [Fact]
    public void GivenAQueryTooLongToRepeatAndMoreMatchesThanAPage_WhenSearched_ThenThePageFitsAndNextIsLeftOutWithHowToResume()
    {
        using TestFolder folder = ManyObservations();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Unknown(1000), Count: 10, Lenient: true));

        JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeJson.Compact).Length.ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        int returned = envelope.Facts.ShouldNotBeNull().Returned;
        returned.ShouldBeGreaterThan(0);
        envelope.Next.ShouldBeEmpty();
        envelope.Omitted.ShouldContain(item => item.What == $"{30 - returned} more matches (offset={returned}; no next call, as the query is too long to repeat in one)");
    }

    [Fact]
    public void GivenAShortQueryAndMoreMatchesThanAPage_WhenSearched_ThenNextResumesIt()
    {
        using TestFolder folder = ManyObservations();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code=8867-4", Count: 10));

        envelope.Next.ShouldNotBeEmpty();
        envelope.Omitted.ShouldContain(item => item.What == "20 more matches (offset=10)");
    }
}
