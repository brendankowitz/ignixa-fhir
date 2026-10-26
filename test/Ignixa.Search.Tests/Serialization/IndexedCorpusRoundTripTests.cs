// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Serialization;
using Ignixa.Search.Tests.Indexing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Serialization;

/// <summary>
/// Guard for the compact format round trip: every value the production indexer
/// (with the in-memory <c>_lastUpdated</c> and canonical entries) extracts from a realistic corpus survives the compact
/// format unchanged, each value in an index of its own and as a whole index.
/// </summary>
public class IndexedCorpusRoundTripTests
{
    private static readonly JsonSerializerOptions IndexOptions = new() { Converters = { new CompactSearchIndexConverter() } };

    private static readonly string[] Corpus =
    [
        """{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"2024-02-03T04:05:06.789-05:30","profile":["http://example.org/fhir/StructureDefinition/p|2.0","http://example.org/profiles/q"]},"identifier":[{"type":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v2-0203","code":"MR"}]},"system":"http://hospital/mrn","value":"A,1|2$3\\4"}],"active":true,"name":[{"family":"Ödegaard","given":["Ann","Marie"]},{"text":"Mixed Case Text"}],"telecom":[{"system":"phone","value":"555-1234","use":"home"},{"system":"email","value":"a@example.org"}],"gender":"female","birthDate":"0042","deceasedDateTime":"2020-01-02T03:04:05-05:30","address":[{"line":["1 Main St"],"city":"Springfield","postalCode":"12345"}],"generalPractitioner":[{"reference":"urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"},{"reference":"http://other.example/fhir/Practitioner/x"},{"reference":"Organization/o1"}]}""",
        """{"resourceType":"Observation","id":"o1","status":"final","category":[{"text":"Vital Signs"}],"code":{"coding":[{"system":"http://loinc.org","code":"85354-9","display":"Blood pressure"}],"text":"Blood Pressure"},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"0999-12-31T23:59:59.1234567-00:30","valueQuantity":{"value":185.0,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"},"component":[{"code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},"valueQuantity":{"value":107,"unit":"mmHg","system":"http://unitsofmeasure.org","code":"mm[Hg]"}},{"code":{"text":"text only"},"valueQuantity":{"value":60,"code":"mm[Hg]"}}]}""",
        """{"resourceType":"Observation","id":"o2","status":"final","code":{"coding":[{"code":"x"}]},"effectivePeriod":{"start":"2021-01-01","end":"2021-03"},"valueString":"a|b,c$d"}""",
        """{"resourceType":"Observation","id":"o3","status":"final","code":{"text":"range"},"valueRange":{"high":{"value":5,"unit":"milligram","system":"http://unitsofmeasure.org","code":"mg"}}}""",
        """{"resourceType":"RiskAssessment","id":"r1","status":"final","subject":{"reference":"Patient/p1"},"prediction":[{"probabilityRange":{"low":{"value":0.2},"high":{"value":0.5}}},{"probabilityDecimal":0.75}]}""",
        """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"period":{"start":"2020-03-04T05:06:07-03:30","end":"2020-03-04T06:00:00Z"},"reasonCode":[{"text":"Annual Check-up Visit"}]}""",
        """{"resourceType":"CarePlan","id":"c1","status":"active","intent":"plan","subject":{"reference":"Patient/p1"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd|1#frag"],"period":{"start":"2020"}}""",
        """{"resourceType":"DocumentReference","id":"d1","status":"current","content":[{"attachment":{"url":"http://example.org/a.pdf"}}],"relatesTo":[{"code":"replaces","target":{"reference":"DocumentReference/d0"}}]}""",
        """{"resourceType":"ValueSet","id":"v1","status":"active","url":"http://example.org/ValueSet/x","version":"1.0"}""",
    ];

    [Fact]
    public void GivenARealisticCorpus_WhenEveryIndexedValueRoundTripsAlone_ThenNoneDiffers()
    {
        List<(string Where, SearchIndexEntry Entry)> values = [.. IndexCorpus().SelectMany(resource => resource.Index.Select(entry => ($"{resource.Id} {entry.SearchParameter.Code}", entry)))];

        List<string> mismatches = [.. values
            .Select(item => (item.Where, Written: SearchValueCanon.Describe(item.Entry.Value), Loaded: SearchValueCanon.Describe(RoundTripAlone(item.Entry))))
            .Where(item => item.Written != item.Loaded)
            .Select(item => $"{item.Where}\n  wrote  {item.Written}\n  loaded {item.Loaded}")];

        values.Count.ShouldBeGreaterThan(80);
        values.Select(item => item.Entry.Value.GetType()).Distinct().Count().ShouldBe(8, "the corpus must exercise every persisted value type");
        mismatches.ShouldBeEmpty();
    }

    private static ISearchValue RoundTripAlone(SearchIndexEntry entry) =>
        JsonSerializer.Deserialize<List<SearchIndexEntry>>(JsonSerializer.Serialize(new List<SearchIndexEntry> { entry }, IndexOptions), IndexOptions)!
            .ShouldHaveSingleItem().Value;

    [Fact]
    public void GivenARealisticCorpus_WhenEachResourcesIndexRoundTrips_ThenEveryEntryIsUnchangedAndInOrder()
    {
        foreach ((string id, IReadOnlyCollection<SearchIndexEntry> index) in IndexCorpus())
        {
            List<SearchIndexEntry> written = [.. index];
            List<SearchIndexEntry> loaded = JsonSerializer.Deserialize<List<SearchIndexEntry>>(JsonSerializer.Serialize(written, IndexOptions), IndexOptions)!;

            // The index groups values by parameter code, so it is compared per code in extraction order.
            Describe(loaded).ShouldBe(Describe(written), id);
        }
    }

    private static IEnumerable<(string Id, IReadOnlyCollection<SearchIndexEntry> Index)> IndexCorpus()
    {
        VersionedCorpus r4 = VersionedCorpus.For(FhirVersion.R4);
        ISearchIndexer indexer = new LastUpdatedSearchIndexer(
            new CanonicalReferenceSearchIndexer(r4.Indexer, r4.Definitions),
            r4.Definitions,
            NullLogger.Instance);
        return Corpus.Select(json => r4.Index(indexer, json)).Select(resource => (resource.Location.Id, resource.Index));
    }

    private static List<string> Describe(IEnumerable<SearchIndexEntry> entries) =>
        [.. entries.GroupBy(entry => entry.SearchParameter.Code).OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group.Select(entry => $"{group.Key} {SearchValueCanon.Describe(entry.Value)}"))];
}
