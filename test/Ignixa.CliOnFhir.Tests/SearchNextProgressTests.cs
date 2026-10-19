// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 2, Q3: a page's <c>next</c> always moves on. The next calls repeat the request, so their room can leave none
/// for a resource; a page of none then left <c>next</c> pointing at the same offset, which a client would call for ever.
/// Regression: at dc1b5418 some budgets gave an empty page whose next resumed at its own offset.
/// </summary>
public class SearchNextProgressTests
{
    // Sixty observations of about 900 bytes each, and a query of about 200 characters that matches them all.
    private static TestFolder Readings() => new TestFolder().Write("patient.ndjson", """{"resourceType":"Patient","id":"p1"}""").Write("obs.ndjson", string.Join('\n', Enumerable.Range(1, 60).Select(index =>
        $$$"""{"resourceType":"Observation","id":"o{{{index}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":{{{index}}},"unit":"/min"},"note":[{"text":"{{{new string('n', 700)}}}"}]}""")));

    // A budget where, at dc1b5418, the table's next calls left no room for a row.
    private const int TableBudget = 3200;

    private static readonly string Query = "code=" + string.Join(',', Enumerable.Repeat("http://loinc.org|8867-4", 8));

    private static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenEveryBudgetFromTheSmallestUp_WhenSearched_ThenAPageWithANextReturnsAResourceAndNextMovesPastIt(bool asTable)
    {
        IReadOnlyList<SearchColumn>? columns = asTable ? SearchColumn.From(new Dictionary<string, string> { ["note"] = "note.text", ["v"] = "value.ofType(Quantity).value" }) : null;
        using TestFolder folder = Readings();
        FolderSession session = Open(folder);

        List<string> stalled = [];
        for (int maxBytes = EnvelopeJson.MinBudget; maxBytes <= 8192; maxBytes += 16)
        {
            ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("Observation", Query, Offset: 3, MaxBytes: maxBytes, Columns: columns));
            SearchFacts facts = envelope.Facts.ShouldNotBeNull();
            bool skipped = envelope.Omitted.Any(item => item.What.Contains("exceeds the size budget", StringComparison.Ordinal));
            if (envelope.Next.Count > 0 && facts.Returned == 0 && !skipped && !envelope.Omitted.Any(item => item.What.StartsWith("the row of", StringComparison.Ordinal)))
            {
                stalled.Add($"maxBytes={maxBytes}: no resource returned beside {envelope.Next.Count} next calls");
            }

            stalled.AddRange(envelope.Next.Where(call => Repeats(call, maxBytes, asTable)).Select(call => $"maxBytes={maxBytes}: next repeats the request at offset {call.Args["offset"]}"));
        }

        stalled.ShouldBeEmpty();
    }

    // A call to make as it is (no hint asks for an edit) that is this request again, at an offset that does not move past this
    // page's start (3).
    private static bool Repeats(NextCall call, int maxBytes, bool asTable) =>
        call.Hint is null && call.Args.TryGetValue("offset", out object? offset) && Convert.ToInt32(offset, System.Globalization.CultureInfo.InvariantCulture) <= 3
        && call.Args.Keys.Order(StringComparer.Ordinal).SequenceEqual(asTable ? ["columns", "maxBytes", "offset", "query", "resourceType"] : ["maxBytes", "offset", "query", "resourceType"])
        && Equals(call.Args["query"], Query) && Convert.ToInt32(call.Args["maxBytes"], System.Globalization.CultureInfo.InvariantCulture) == maxBytes;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenABudgetWhereTheNextCallsLeaveNoRoom_WhenSearched_ThenTheyAreLeftOutForAPageThatMovesOn(bool asTable)
    {
        using TestFolder folder = Readings();
        IReadOnlyList<SearchColumn>? columns = asTable ? SearchColumn.From(new Dictionary<string, string> { ["note"] = "note.text", ["v"] = "value.ofType(Quantity).value" }) : null;

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Query, Offset: 3, MaxBytes: asTable ? TableBudget : 4096, Columns: columns));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBeGreaterThan(0);
        envelope.Next.ShouldBeEmpty();
        envelope.Omitted.ShouldContain(item => item.What.Contains("no next call", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenEveryBudgetFromTheSmallestUp_WhenFoundInAPatientsContext_ThenNextAlwaysMovesPastThePage()
    {
        using TestFolder folder = Readings();
        FolderSession session = Open(folder);

        List<string> stalled = [];
        for (int maxBytes = EnvelopeJson.MinBudget; maxBytes <= 8192; maxBytes += 16)
        {
            ToolEnvelope<Patients.FindFacts?> envelope = Patients.ContextFinder.Find(session, "p1", "8867-4", offset: 3, maxBytes: maxBytes);
            stalled.AddRange(envelope.Next
                .Where(call => call.Hint is null && call.Args.TryGetValue("offset", out object? offset) && Convert.ToInt32(offset, System.Globalization.CultureInfo.InvariantCulture) <= 3)
                .Select(call => $"maxBytes={maxBytes}: returned {envelope.Facts?.Returned}, next repeats offset {call.Args["offset"]}"));
        }

        stalled.ShouldBeEmpty();
    }

    [Fact]
    public void GivenABudgetWhereNoResourceFits_WhenSearched_ThenThereIsNoNextAndOmittedSaysWhatToNarrow()
    {
        using TestFolder folder = Readings();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Query, MaxBytes: EnvelopeJson.MinBudget));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(0);
        envelope.Next.ShouldBeEmpty();
        envelope.Omitted.ShouldContain(item => item.What.Contains("offset=0; not one fits the size budget", StringComparison.Ordinal));
    }
}
