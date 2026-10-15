// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// References to Bundle entries by fullUrl (<c>urn:uuid:</c> or absolute) resolved to the catalogued <c>Type/id</c> in the
/// folder index, and the <c>unresolved-bundle-reference</c> issue for those that name no entry.
/// </summary>
public class BundleReferenceTests
{
    private const string UnresolvedCode = "unresolved-bundle-reference";

    /// <summary>A transaction Bundle: two patients by urn:uuid, two encounters of the first (one by urn, one relative), one of the second.</summary>
    private static TestFolder Transaction() => new TestFolder().Write("bundle.json", """
        {"resourceType":"Bundle","type":"transaction","entry":[
          {"fullUrl":"urn:uuid:aaaa-1","resource":{"resourceType":"Patient","id":"pa","gender":"male","name":[{"family":"Ames"}]},"request":{"method":"POST","url":"Patient"}},
          {"fullUrl":"urn:uuid:bbbb-2","resource":{"resourceType":"Patient","id":"pb","gender":"female","name":[{"family":"Bell"}]},"request":{"method":"POST","url":"Patient"}},
          {"fullUrl":"urn:uuid:e-1","resource":{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:aaaa-1"}},"request":{"method":"POST","url":"Encounter"}},
          {"resource":{"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:bbbb-2"}},"fullUrl":"urn:uuid:e-2","request":{"method":"POST","url":"Encounter"}},
          {"fullUrl":"urn:uuid:e-3","resource":{"resourceType":"Encounter","id":"e3","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/pa"}},"request":{"method":"POST","url":"Encounter"}},
          {"fullUrl":"urn:uuid:o-1","resource":{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:aaaa-1"},"encounter":{"reference":"urn:uuid:e-1"}},"request":{"method":"POST","url":"Observation"}}
        ]}
        """);

    // Regression: at 2e56e3f the urn was indexed verbatim, so subject=Patient/pa matched only the relative reference (e3).
    [Theory]
    [InlineData("subject=Patient/pa", "e1,e3")]
    [InlineData("subject=Patient/pb", "e2")]
    [InlineData("subject:Patient.gender=male", "e1,e3")]
    [InlineData("subject:Patient.family=Bell", "e2")]
    public void GivenATransactionBundle_WhenEncountersAreSearchedThroughTheirSubject_ThenUrnReferencesResolveToTheEntries(string query, string expected)
    {
        using TestFolder folder = Transaction();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", query));

        Ids(envelope).ShouldBe(expected.Split(','));
        envelope.Issues.ShouldBeEmpty();
    }

    // Regression: at 2e56e3f the indexer typed the urn under single-target Observation.encounter (Encounter/urn:uuid:e-1),
    // and indexed nothing for patient, whose expression (subject.where(resolve() is Patient)) cannot resolve a urn.
    [Theory]
    [InlineData("encounter=Encounter/e1")]
    [InlineData("encounter:Encounter.subject=Patient/pa")]
    [InlineData("patient=Patient/pa")]
    public void GivenAUrnUnderASingleTargetParameter_WhenSearched_ThenItResolvesLikeAnUntypedOne(string query)
    {
        using TestFolder folder = Transaction();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        Ids(envelope).ShouldBe(["o1"]);
        envelope.Issues.ShouldBeEmpty();
    }

    // Regression: at 2e56e3f the include followed nothing and _has matched no patient.
    [Fact]
    public void GivenATransactionBundle_WhenIncludedOrReverseChained_ThenTheResolvedReferencesAreFollowed()
    {
        using TestFolder folder = Transaction();
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> included = ResourceSearcher.Search(session, new SearchRequest("Encounter", "_id=e2&_include=Encounter:subject"));
        ToolEnvelope<SearchFacts?> reversed = ResourceSearcher.Search(session, new SearchRequest("Patient", "_has:Encounter:subject:_id=e1"));

        included.Facts.ShouldNotBeNull().Included.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["pb"]);
        included.Issues.ShouldBeEmpty();
        Ids(reversed).ShouldBe(["pa"]);
    }

    // Regression: at 2e56e3f an unresolved urn was counted only when a chain or include followed it, as references-unresolved.
    [Fact]
    public void GivenAUrnNoEntryNames_WhenTheTypeIsSearched_ThenItStaysVerbatimAndOneInformationIssueCountsIt()
    {
        using TestFolder folder = new TestFolder().Write("enc.ndjson", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:gone-1"}}
            {"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Encounter","id":"e3","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:gone-2"},"participant":[{"individual":{"reference":"urn:uuid:gone-3"}}]}
            """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "subject=urn:uuid:gone-2"));

        Ids(envelope).ShouldBe(["e3"]);
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("information");
        issue.Code.ShouldBe(UnresolvedCode);
        issue.Location.ShouldBe("enc.ndjson:1");

        // Counted per reference element, distinct within a resource, however many parameters index it (subject and patient).
        issue.Diagnostics.ShouldStartWith("3 urn: references in Encounter resources name no Bundle entry fullUrl in the folder");
        issue.Diagnostics.ShouldEndWith(": Encounter/e1 -> urn:uuid:gone-1, Encounter/e3 -> urn:uuid:gone-2, Encounter/e3 -> urn:uuid:gone-3.");
    }

    // Regression: at 2e56e3f no issue counted unresolved urns; the sample list stops at five and says so.
    [Fact]
    public void GivenMoreUnresolvedUrnsThanTheSampleLimit_WhenSearched_ThenTheIssueCountsAllAndNamesTheFirstFive()
    {
        using TestFolder folder = new TestFolder().Write("enc.ndjson", string.Join('\n', Enumerable.Range(1, 7).Select(i =>
            "{\"resourceType\":\"Encounter\",\"id\":\"e" + i + "\",\"status\":\"finished\",\"class\":{\"code\":\"AMB\"},\"subject\":{\"reference\":\"urn:uuid:gone-" + i + "\"}}")));

        IssueItem issue = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter")).Issues.ShouldHaveSingleItem();

        issue.Diagnostics.ShouldStartWith("7 urn: references in Encounter resources");
        issue.Diagnostics.ShouldContain("(first 5): Encounter/e1 -> urn:uuid:gone-1,");
        issue.Diagnostics.ShouldEndWith("Encounter/e5 -> urn:uuid:gone-5.");
    }

    // Regression: a urn resolves within its own Bundle first; one two Bundles give to different resources is ambiguous elsewhere.
    [Fact]
    public void GivenTwoBundlesReusingAFullUrl_WhenSearched_ThenEachBundleResolvesItsOwnAndAResourceOutsideBothResolvesNeither()
    {
        using TestFolder folder = new TestFolder()
            .Write("b1.json", """
                {"resourceType":"Bundle","type":"collection","entry":[
                  {"fullUrl":"urn:uuid:same","resource":{"resourceType":"Patient","id":"p-one"}},
                  {"fullUrl":"urn:uuid:o1","resource":{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:same"}}}
                ]}
                """)
            .Write("b2.json", """
                {"resourceType":"Bundle","type":"collection","entry":[
                  {"fullUrl":"urn:uuid:same","resource":{"resourceType":"Patient","id":"p-two"}},
                  {"fullUrl":"urn:uuid:o2","resource":{"resourceType":"Observation","id":"o2","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:same"}}}
                ]}
                """)
            .Write("loose.ndjson", """
                {"resourceType":"Observation","id":"o3","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:same"}}
                {"resourceType":"Observation","id":"o4","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:o1"}}
                """);
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> one = ResourceSearcher.Search(session, new SearchRequest("Observation", "subject=Patient/p-one"));
        ToolEnvelope<SearchFacts?> two = ResourceSearcher.Search(session, new SearchRequest("Observation", "subject=Patient/p-two"));
        ToolEnvelope<SearchFacts?> unique = ResourceSearcher.Search(session, new SearchRequest("Observation", "subject=Observation/o1"));

        Ids(one).ShouldBe(["o1"]);
        Ids(two).ShouldBe(["o2"]);
        Ids(unique).ShouldBe(["o4"]);
        one.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("Observation/o3 -> urn:uuid:same");
    }

    // Regression: at 2e56e3f an absolute reference was never followed by a chain or include, even when an entry's fullUrl named it.
    [Fact]
    public void GivenAnAbsoluteFullUrl_WhenAReferenceNamesIt_ThenItResolvesAndAnAbsoluteReferenceNoEntryNamesStaysExternal()
    {
        using TestFolder folder = new TestFolder().Write("bundle.json", """
            {"resourceType":"Bundle","type":"collection","entry":[
              {"fullUrl":"http://example.org/fhir/Patient/px","resource":{"resourceType":"Patient","id":"px","gender":"male"}},
              {"fullUrl":"http://example.org/fhir/Encounter/e1","resource":{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"http://example.org/fhir/Patient/px/_history/2"}}},
              {"fullUrl":"http://example.org/fhir/Encounter/e2","resource":{"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"subject":{"reference":"http://elsewhere.example.org/fhir/Patient/px"}}}
            ]}
            """);

        ToolEnvelope<SearchFacts?> chained = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "subject:Patient.gender=male"));

        Ids(chained).ShouldBe(["e1"]);
        chained.Issues.ShouldHaveSingleItem().Diagnostics.ShouldStartWith("1 references through Encounter:subject are not local");
    }

    // Regression: composite components are references too (R5 DocumentReference-relationship: relatesto$relation).
    [Fact]
    public void GivenAUrnInsideACompositeValue_WhenTheCompositeIsSearched_ThenTheComponentIsResolved()
    {
        using TestFolder folder = new TestFolder().Write("bundle.json", """
            {"resourceType":"Bundle","type":"collection","entry":[
              {"fullUrl":"urn:uuid:d0","resource":{"resourceType":"DocumentReference","id":"dr0","status":"current","content":[{"attachment":{"url":"http://example.org/a"}}]}},
              {"fullUrl":"urn:uuid:d1","resource":{"resourceType":"DocumentReference","id":"dr1","status":"current","content":[{"attachment":{"url":"http://example.org/b"}}],
                "relatesTo":[{"code":{"coding":[{"system":"http://hl7.org/fhir/document-relationship-type","code":"replaces"}]},"target":{"reference":"urn:uuid:d0"}}]}}
            ]}
            """);
        var session = new FolderSession(folder.Path, new R5CoreSchemaProvider(), NullLoggerFactory.Instance);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("DocumentReference", "relationship=DocumentReference/dr0$http://hl7.org/fhir/document-relationship-type|replaces"));

        Ids(envelope).ShouldBe(["dr1"]);
        envelope.Issues.ShouldBeEmpty();
    }

    // Regression: the resolved index and its diagnostic are what the cache persists and reloads (at 2e56e3f: neither existed).
    [Fact]
    public void GivenResolvedReferences_WhenTheIndexIsReloadedFromTheCache_ThenTheResolutionAndTheIssueSurvive()
    {
        using TestFolder folder = Transaction().Write("loose.ndjson", """
            {"resourceType":"Encounter","id":"e9","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:gone"}}
            """);
        using var cache = new TestFolder();
        FolderSession writer = OpenCached(folder, cache);
        writer.GetIndex("Encounter").FromCache.ShouldBeFalse();

        FolderSession reader = OpenCached(folder, cache);
        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(reader, new SearchRequest("Encounter", "subject=Patient/pa"));

        reader.GetIndex("Encounter").FromCache.ShouldBeTrue();
        Ids(envelope).ShouldBe(["e1", "e3"]);
        envelope.Issues.Select(issue => issue.Code).ShouldBe([UnresolvedCode]);
    }

    // Regression: a Bundle edit changes what another file's references resolve to, and the manifest lists every file.
    [Fact]
    public void GivenACachedIndex_WhenAnotherFilesBundleChanges_ThenTheIndexIsRebuiltWithTheNewResolution()
    {
        using TestFolder folder = new TestFolder()
            .Write("enc.ndjson", """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:later"}}""")
            .Write("bundle.json", """{"resourceType":"Bundle","type":"collection","entry":[]}""");
        using var cache = new TestFolder();
        Ids(ResourceSearcher.Search(OpenCached(folder, cache), new SearchRequest("Encounter", "subject=Patient/late"))).ShouldBeEmpty();

        folder.Write("bundle.json", """{"resourceType":"Bundle","type":"collection","entry":[{"fullUrl":"urn:uuid:later","resource":{"resourceType":"Patient","id":"late"}}]}""");
        FolderSession reopened = OpenCached(folder, cache);
        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(reopened, new SearchRequest("Encounter", "subject=Patient/late"));

        reopened.GetIndex("Encounter").FromCache.ShouldBeFalse();
        Ids(envelope).ShouldBe(["e1"]);
    }

    private static FolderSession OpenCached(TestFolder folder, TestFolder cache) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cache.Path));
}
