// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// What a search matches. These pin the semantics of fhir-candle's interpreter (same index entry, field-aware, range-aware)
/// where Ignixa's stock in-memory interpreter returned wrong results without any issue.
/// </summary>
public class SearchSemanticsTests
{
    private const string TwoIdentifiers = """
        {"resourceType":"Patient","id":"a","identifier":[{"system":"http://hospital/mrn","value":"111"},{"system":"http://hl7.org/fhir/sid/us-ssn","value":"555"}]}
        {"resourceType":"Patient","id":"b","identifier":[{"system":"http://hospital/mrn","value":"555"}]}
        """;

    private const string TwoCodings = """
        {"resourceType":"Observation","id":"mixed","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"1234-5"},{"system":"http://snomed.info/sct","code":"8867-4"}]}}
        {"resourceType":"Observation","id":"hr","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}
        """;

    [Theory]
    [InlineData("identifier=http://hospital/mrn|555", "b")]
    [InlineData("identifier=http://hospital/mrn|111", "a")]
    [InlineData("identifier=http://hl7.org/fhir/sid/us-ssn|555", "a")]
    [InlineData("identifier:not=http://hospital/mrn|555", "a")]
    [InlineData("identifier=555", "a,b")]
    public void GivenTwoIdentifiers_WhenSearchedBySystemAndValue_ThenSystemAndValueMustComeFromTheSameIdentifier(string query, string expected)
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", TwoIdentifiers);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", query));

        Ids(envelope).ShouldBe(expected.Split(','));
        envelope.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("code=http://loinc.org|8867-4", "hr")]
    [InlineData("code=http://snomed.info/sct|8867-4", "mixed")]
    [InlineData("code:not=http://loinc.org|8867-4", "mixed")]
    [InlineData("code=8867-4", "mixed,hr")]
    public void GivenTwoCodings_WhenSearchedBySystemAndCode_ThenSystemAndCodeMustComeFromTheSameCoding(string query, string expected)
    {
        using TestFolder folder = new TestFolder().Write("o.ndjson", TwoCodings);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        Ids(envelope).ShouldBe(expected.Split(','));
    }

    [Theory]
    [InlineData("ge2021-01-01T00:15:00Z", true)]
    [InlineData("gt2021-01-01T00:15:00Z", true)]
    [InlineData("le2021-01-01T00:15:00Z", true)]
    [InlineData("lt2021-01-01T00:15:00Z", true)]
    [InlineData("eq2021-01-01T00:00:00Z", false)]
    [InlineData("ne2021-01-01T00:00:00Z", true)]
    [InlineData("gt2021-01-01T01:00:00Z", false)]
    [InlineData("lt2020-12-31T23:00:00Z", false)]
    [InlineData("ge2021-01-01T00:30:00Z", true)]
    public void GivenAPeriod_WhenSearchedWithDatePrefixes_ThenBothBoundsOfThePeriodAreConsidered(string value, bool matches)
    {
        using TestFolder folder = new TestFolder().Write(
            "e.json",
            """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"period":{"start":"2021-01-01T00:00:00Z","end":"2021-01-01T00:30:00Z"}}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", $"date={value}"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(matches ? 1 : 0);
    }

    [Theory]
    [InlineData("birthdate=ge1980-06-01", true)]
    [InlineData("birthdate=gt1980", false)]
    [InlineData("birthdate=eq1980", true)]
    [InlineData("birthdate=lt1980", false)]
    [InlineData("birthdate=le1980-01-01", true)]
    public void GivenPartialDate_WhenSearched_ThenItCoversTheWholeYear(string query, bool matches)
    {
        using TestFolder folder = new TestFolder().Write("p.json", """{"resourceType":"Patient","id":"p1","birthDate":"1980"}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", query));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(matches ? 1 : 0);
    }

    [Fact]
    public void GivenPeriodsAndInstants_WhenSearchedByDate_ThenPeriodsMatchByOverlapAndEqualityNeedsContainment()
    {
        using TestFolder folder = new TestFolder().Write("o.ndjson", """
            {"resourceType":"Observation","id":"long","status":"final","code":{"text":"x"},"effectivePeriod":{"start":"1979-01-01","end":"1985-01-01"}}
            {"resourceType":"Observation","id":"q1","status":"final","code":{"text":"x"},"effectivePeriod":{"start":"2020-01-01T10:00:00Z","end":"2020-03-01T00:00:00Z"}}
            {"resourceType":"Observation","id":"day","status":"final","code":{"text":"x"},"effectiveDateTime":"2020-01-01T10:00:00Z"}
            """);
        FolderSession session = Open(folder);

        Ids(ResourceSearcher.Search(session, new SearchRequest("Observation", "date=gt1980"))).ShouldBe(["long", "q1", "day"]);
        Ids(ResourceSearcher.Search(session, new SearchRequest("Observation", "date=eq2020-01-01"))).ShouldBe(["day"]);
    }

    [Fact]
    public void GivenTokenTextModifier_WhenSearched_ThenDisplayAndTextAreMatchedNotTheCode()
    {
        using TestFolder folder = new TestFolder().Write("o.ndjson", """
            {"resourceType":"Observation","id":"t1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4","display":"Heart rate"}],"text":"Heart rate"}}
            {"resourceType":"Observation","id":"t2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"heart"}]}}
            """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code:text=heart"));

        Ids(envelope).ShouldBe(["t1"]);
    }

    [Theory]
    [InlineData("family=odegaard")]
    [InlineData("family=Ödegaard")]
    [InlineData("family=ODEG")]
    public void GivenAccentedName_WhenSearchedWithoutAccents_ThenItMatches(string query)
    {
        using TestFolder folder = new TestFolder().Write("p.json", """{"resourceType":"Patient","id":"p3","name":[{"family":"Ödegaard"}]}""");

        Ids(ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", query))).ShouldBe(["p3"]);
    }

    [Fact]
    public void GivenLastUpdated_WhenSearched_ThenItIsMatchedFromMetaLastUpdated()
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", """
            {"resourceType":"Patient","id":"old","meta":{"lastUpdated":"2019-05-01T00:00:00Z"}}
            {"resourceType":"Patient","id":"new","meta":{"lastUpdated":"2022-05-01T00:00:00Z"}}
            {"resourceType":"Patient","id":"none"}
            """);
        FolderSession session = Open(folder);

        Ids(ResourceSearcher.Search(session, new SearchRequest("Patient", "_lastUpdated=gt2020-01-01"))).ShouldBe(["new"]);
        Ids(ResourceSearcher.Search(session, new SearchRequest("Patient", "_lastUpdated=lt2020-01-01"))).ShouldBe(["old"]);
    }

    [Fact]
    public void GivenNegatedId_WhenSearched_ThenTheNamedResourcesAreExcluded()
    {
        using TestFolder folder = Clinic();

        Ids(ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id:not=p1"))).ShouldBe(["p2", "p3"]);
    }

    [Fact]
    public void GivenPersistedIndex_WhenSearchedFromTheCache_ThenIncludingLastUpdatedTheResultsAreTheSame()
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", """
            {"resourceType":"Patient","id":"old","meta":{"lastUpdated":"2019-05-01T00:00:00Z"},"name":[{"family":"Smith"}]}
            {"resourceType":"Patient","id":"new","meta":{"lastUpdated":"2022-05-01T00:00:00Z"},"name":[{"family":"Jones"}]}
            """);
        using TestFolder cache = new();
        FolderSession Open() => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cache.Path));
        FolderSession built = Open();
        ResourceSearcher.Search(built, new SearchRequest("Patient")).Facts.ShouldNotBeNull();

        FolderSession loaded = Open();
        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(loaded, new SearchRequest("Patient", "_lastUpdated=gt2020-01-01&family=Jones"));

        loaded.GetIndex("Patient").FromCache.ShouldBeTrue();
        Ids(envelope).ShouldBe(["new"]);
    }

    [Fact]
    public void GivenFilterParameter_WhenSearched_ThenTheCallFailsNamingTheParameterInsteadOfThrowing()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_filter=name eq x&family=Smith"));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("invalid-search-request");
        issue.Diagnostics.ShouldContain("_filter=name eq x");
        issue.Diagnostics.ShouldNotContain("family");
    }

    [Fact]
    public void GivenCompositeParameter_WhenSearched_ThenItIsEvaluatedPerComponent()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code-value-quantity=http://loinc.org|8867-4$gt70"));

        Ids(envelope).ShouldBe(["o2", "o3", "o5"]);
    }

    [Fact]
    public void GivenMalformedCompositeValue_WhenSearched_ThenTheCallFailsNamingTheParameter()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code-value-quantity=http://loinc.org|8867-4$gtnotanumber"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("code-value-quantity");
    }

    [Fact]
    public void GivenFolderThatDoesNotExist_WhenSearchedOrRead_ThenTheCallFailsWithAFolderIssue()
    {
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"), "nope");
        var session = new FolderSession(missing, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

        ToolEnvelope<SearchFacts?> search = ResourceSearcher.Search(session, new SearchRequest("Patient"));
        ToolEnvelope<Reading.ReadFacts?> read = Reading.ResourceFetcher.Read(session, ["Patient/p1"]);

        search.Facts.ShouldBeNull();
        search.Issues.ShouldHaveSingleItem().Code.ShouldBe("folder-unavailable");
        search.Freshness.IndexBuiltAt.ShouldBeNull();
        read.Facts.ShouldBeNull();
        read.Issues.ShouldHaveSingleItem().Code.ShouldBe("folder-unavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("code,status")]
    public void GivenMaximumPageOfLargeResources_WhenSearched_ThenTheWholeSerializedResponseStaysWithinTheCap(string? elements)
    {
        string notes = new('n', 1500);
        using TestFolder folder = new TestFolder().Write(
            "a-deliberately/long/directory/name/for/evidence/entries/observations.ndjson",
            string.Join('\n', Enumerable.Range(0, 80).Select(i =>
                "{\"resourceType\":\"Observation\",\"id\":\"obs-" + i + "\",\"status\":\"final\",\"code\":{\"text\":\"x\"},\"note\":[{\"text\":\"" + notes + "\"}]}")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Count: 50, Elements: elements));

        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(80);
        envelope.Omitted.ShouldNotBeEmpty();
        envelope.Evidence.Count.ShouldBe(envelope.Facts.Returned);
        if (elements is null)
        {
            envelope.Facts.Returned.ShouldBeLessThan(50);
            envelope.Omitted[^1].Why.ShouldBe("size-budget");
        }
    }
}
