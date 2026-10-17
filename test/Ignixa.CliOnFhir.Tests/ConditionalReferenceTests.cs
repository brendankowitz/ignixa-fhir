// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Conditional references (<c>Type?identifier=system|value</c>, as Synthea writes practitioners, organizations and locations)
/// resolved to the one resource of that type the folder holds with that identifier, and the <c>unresolved-bundle-reference</c>
/// issue, with its reason, for those that match none, several, or use criteria other than one identifier.
/// </summary>
public class ConditionalReferenceTests
{
    private const string UnresolvedCode = "unresolved-bundle-reference";

    private const string Requester = "Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111";

    private const string Provider = "Organization?identifier=https://github.com/synthetichealth/synthea|org-1";

    /// <summary>
    /// Synthea's shape: a batch Bundle of practitioners, organizations and locations (two organizations share an identifier),
    /// and a patient's transaction Bundle referencing them conditionally and its own entries by urn:uuid.
    /// </summary>
    private static TestFolder SyntheaShaped() => new TestFolder()
        .Write("practitionerInformation.json", """
            {"resourceType":"Bundle","type":"batch","entry":[
              {"fullUrl":"urn:uuid:pr-1","resource":{"resourceType":"Practitioner","id":"pr1","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"111"}],"name":[{"family":"Marquardt","given":["Bettina"]}]},"request":{"method":"POST","url":"Practitioner","ifNoneExist":"identifier=http://hl7.org/fhir/sid/us-npi|111"}},
              {"fullUrl":"urn:uuid:org-1","resource":{"resourceType":"Organization","id":"org1","identifier":[{"system":"https://github.com/synthetichealth/synthea","value":"org-1"}],"name":"General Hospital"},"request":{"method":"POST","url":"Organization"}},
              {"fullUrl":"urn:uuid:org-2","resource":{"resourceType":"Organization","id":"org2","identifier":[{"system":"https://github.com/synthetichealth/synthea","value":"org-dup"}],"name":"Twin One"},"request":{"method":"POST","url":"Organization"}},
              {"fullUrl":"urn:uuid:org-3","resource":{"resourceType":"Organization","id":"org3","identifier":[{"system":"https://github.com/synthetichealth/synthea","value":"org-dup"}],"name":"Twin Two"},"request":{"method":"POST","url":"Organization"}},
              {"fullUrl":"urn:uuid:loc-1","resource":{"resourceType":"Location","id":"loc1","identifier":[{"system":"https://github.com/synthetichealth/synthea","value":"loc-1"}],"name":"Ward A"},"request":{"method":"POST","url":"Location"}}
            ]}
            """)
        .Write("patient.json", """
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"fullUrl":"urn:uuid:pa","resource":{"resourceType":"Patient","id":"pa"},"request":{"method":"POST","url":"Patient"}},
              {"fullUrl":"urn:uuid:e-1","resource":{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:pa"},
                "participant":[{"individual":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}}],
                "location":[{"location":{"reference":"Location?identifier=https://github.com/synthetichealth/synthea|loc-1"}}],
                "serviceProvider":{"reference":"Organization?identifier=https://github.com/synthetichealth/synthea|org-1"}},"request":{"method":"POST","url":"Encounter"}},
              {"fullUrl":"urn:uuid:m-1","resource":{"resourceType":"MedicationRequest","id":"m1","status":"active","intent":"order","medicationCodeableConcept":{"text":"x"},
                "subject":{"reference":"urn:uuid:pa"},"encounter":{"reference":"urn:uuid:e-1"},
                "requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}},"request":{"method":"POST","url":"MedicationRequest"}}
            ]}
            """);

    // Regression: at acdd94fc the conditional requester was not followed, with a references-unresolved count giving no reason.
    [Fact]
    public void GivenAConditionalRequester_WhenIncluded_ThenThePractitionerItIdentifiesIsIncludedAndMapped()
    {
        using TestFolder folder = SyntheaShaped();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("MedicationRequest", "_include=MedicationRequest:requester"));

        Included(envelope).ShouldBe(["Practitioner/pr1"]);
        envelope.Issues.ShouldBeEmpty();
        envelope.Facts!.ResolvedReferences.ShouldNotBeNull()["MedicationRequest/m1"][Requester].ShouldBe("Practitioner/pr1");
        ((string)Single(envelope)["requester"]!["reference"]!).ShouldBe(Requester);
    }

    // Regression: at acdd94fc each of these matched nothing, the single-target ones with no issue at all.
    [Theory]
    [InlineData("MedicationRequest", "requester:Practitioner.identifier=http://hl7.org/fhir/sid/us-npi|111", "m1")]
    [InlineData("MedicationRequest", "requester:Practitioner.family=Marquardt", "m1")]
    [InlineData("MedicationRequest", "requester=Practitioner/pr1", "m1")]
    [InlineData("Encounter", "service-provider:Organization.name=General", "e1")]
    [InlineData("Encounter", "service-provider=Organization/org1", "e1")]
    [InlineData("Encounter", "location:Location.name=Ward", "e1")]
    [InlineData("Encounter", "practitioner=Practitioner/pr1", "e1")]
    public void GivenConditionalReferences_WhenSearchedOrChainedThroughThem_ThenTheyMatchTheIdentifiedResource(string type, string query, string expected)
    {
        using TestFolder folder = SyntheaShaped();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest(type, query));

        Ids(envelope).ShouldBe([expected]);
        envelope.Issues.ShouldBeEmpty();
    }

    // Regression: at acdd94fc service-provider, location and practitioner included nothing and reported nothing.
    [Theory]
    [InlineData("Encounter:service-provider", "Organization/org1")]
    [InlineData("Encounter:location", "Location/loc1")]
    [InlineData("Encounter:practitioner", "Practitioner/pr1")]
    [InlineData("Encounter:participant", "Practitioner/pr1")]
    public void GivenConditionalEncounterReferences_WhenIncluded_ThenTheIdentifiedResourceIsIncluded(string include, string expected)
    {
        using TestFolder folder = SyntheaShaped();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", $"_include={include}"));

        Included(envelope).ShouldBe([expected]);
        envelope.Issues.ShouldBeEmpty();
    }

    // The identifier criteria forms a token search accepts: any system, no system, percent-encoded. The resolving rows are
    // regressions (at acdd94fc nothing resolved); the null rows are guards, mutation-checked by ignoring the system.
    [Theory]
    [InlineData("Organization?identifier=org-1", "Organization/org1")]
    [InlineData("Organization?identifier=https%3A%2F%2Fgithub.com%2Fsynthetichealth%2Fsynthea%7Corg-1", "Organization/org1")]
    [InlineData("Organization?identifier=https://github.com/synthetichealth/synthea|org-dup", null)]
    [InlineData("Organization?identifier=|org-1", null)]
    [InlineData("Organization?identifier=http://other.example.org|org-1", null)]
    public void GivenIdentifierCriteriaForms_WhenIncluded_ThenOnlyExactlyOneMatchResolves(string reference, string? expected)
    {
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", Encounter("e9", reference));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "_id=e9&_include=Encounter:service-provider"));

        Included(envelope).ShouldBe(expected is null ? [] : [expected]);
    }

    // Regression: at acdd94fc a conditional reference under the single-target serviceProvider was counted as a local
    // Type/id, so an include or chain through it returned nothing with no issue.
    [Theory]
    [InlineData("Organization?identifier=https://github.com/synthetichealth/synthea|gone", "conditional-no-match")]
    [InlineData("Organization?identifier=https://github.com/synthetichealth/synthea|org-dup", "conditional-ambiguous")]
    [InlineData("Organization?name=General", "conditional-unsupported-parameter")]
    [InlineData("Organization?identifier=https://github.com/synthetichealth/synthea|org-1&active=true", "conditional-unsupported-parameter")]
    [InlineData("Organization?identifier:of-type=http://x|MR|1", "conditional-unsupported-parameter")]
    [InlineData("Organization?identifier=org-1,org-dup", "conditional-unsupported-parameter")]
    public void GivenAConditionalReferenceThatCannotBeResolved_WhenIncluded_ThenOneIssueNamesItAndWhy(string reference, string reason)
    {
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", Encounter("e9", reference));
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> included = ResourceSearcher.Search(session, new SearchRequest("Encounter", "_id=e9&_include=Encounter:service-provider"));
        ToolEnvelope<SearchFacts?> chained = ResourceSearcher.Search(session, new SearchRequest("Encounter", "service-provider:Organization.name=General"));

        Included(included).ShouldBeEmpty();
        IssueItem issue = included.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("information");
        issue.Code.ShouldBe(UnresolvedCode);
        issue.Location.ShouldBe("loose.ndjson:1");
        issue.Diagnostics.ShouldStartWith($"1 conditional references in Encounter resources were not resolved (1 {reason}:");
        issue.Diagnostics.ShouldEndWith($": Encounter/e9 -> {reference} ({reason}).");

        // The resolved encounter still matches; the unresolved one is reported once, by the index, not again by the chain.
        Ids(chained).ShouldBe(["e1"]);
        chained.Issues.Select(item => item.Code).ShouldBe([UnresolvedCode]);
    }

    // Regression: at acdd94fc Encounter:practitioner (participant.individual.where(resolve() is Practitioner)) indexed nothing
    // for a conditional reference, so the include was silently empty; now the index reports each one it could not resolve.
    [Fact]
    public void GivenUnresolvedConditionalsOfEachKind_WhenTheTypeIsSearched_ThenOneIssueCountsEachReasonAndNamesTheFirstFive()
    {
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", string.Join('\n',
            """{"resourceType":"Encounter","id":"e7","status":"finished","class":{"code":"AMB"},"participant":[{"individual":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|999"}}]}""",
            """{"resourceType":"Encounter","id":"e8","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:gone"}}""",
            Encounter("e9", "Organization?identifier=org-dup"),
            Encounter("e10", "Organization?name=Twin"),
            Encounter("e11", "Organization?identifier=gone"),
            Encounter("e12", "Organization?identifier=gone-too")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "_id=e7&_include=Encounter:practitioner"));

        Included(envelope).ShouldBeEmpty();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe(UnresolvedCode);
        issue.Location.ShouldBe("loose.ndjson:1");
        issue.Diagnostics.ShouldStartWith(
            "1 urn: references in Encounter resources name no Bundle entry fullUrl in the folder, or name more than one resource, and "
            + "5 conditional references in Encounter resources were not resolved (3 conditional-no-match: no resource of that type in the folder has the identifier; "
            + "1 conditional-ambiguous: more than one has it; 1 conditional-unsupported-parameter: the criteria are not one 'identifier=[system|]value'), "
            + "so they are indexed as written and reference searches, chains, includes and compartments do not follow them");
        issue.Diagnostics.ShouldEndWith(
            "(first 5): Encounter/e7 -> Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|999 (conditional-no-match), Encounter/e8 -> urn:uuid:gone, "
            + "Encounter/e9 -> Organization?identifier=org-dup (conditional-ambiguous), Encounter/e10 -> Organization?name=Twin (conditional-unsupported-parameter), "
            + "Encounter/e11 -> Organization?identifier=gone (conditional-no-match).");
    }

    // Regression: at acdd94fc a single-target parameter typed any reference text as that type, so a reference whose id is
    // not a FHIR id was counted as a local Type/id and the empty include carried no issue.
    [Fact]
    public void GivenANonLocalReferenceUnderASingleTargetParameter_WhenIncluded_ThenItIsCountedAsUnresolved()
    {
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", Encounter("e9", "not a reference"));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "_id=e9&_include=Encounter:service-provider"));

        Included(envelope).ShouldBeEmpty();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("references-unresolved");
        issue.Diagnostics.ShouldStartWith("1 references through Encounter:service-provider are not local 'Type/id' references");
    }

    // Regression: the resolution and the reasons are what the cache persists and reloads.
    [Fact]
    public void GivenConditionalReferences_WhenTheIndexIsReloadedFromTheCache_ThenTheResolutionAndTheReasonsSurvive()
    {
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", Encounter("e9", "Organization?identifier=org-dup"));
        using var cache = new TestFolder();
        OpenCached(folder, cache).GetIndex("Encounter").FromCache.ShouldBeFalse();

        FolderSession reader = OpenCached(folder, cache);
        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(reader, new SearchRequest("Encounter", "_include=Encounter:service-provider"));

        reader.GetIndex("Encounter").FromCache.ShouldBeTrue();
        Included(envelope).ShouldBe(["Organization/org1"]);
        envelope.Facts!.ResolvedReferences.ShouldNotBeNull()["Encounter/e1"][Provider].ShouldBe("Organization/org1");
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldEndWith(": Encounter/e9 -> Organization?identifier=org-dup (conditional-ambiguous).");
    }

    // Regression: at acdd94fc read_resources resolved nothing in a folder without Bundle fullUrls.
    [Fact]
    public void GivenAConditionalReferenceInAnNdjsonFolder_WhenRead_ThenResolvedReferencesMapItWithoutBuildingAnIndex()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", """
            {"resourceType":"Practitioner","id":"pr1","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"111"}]}
            {"resourceType":"MedicationRequest","id":"m1","status":"active","intent":"order","medicationCodeableConcept":{"text":"x"},"subject":{"reference":"Patient/p1"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}}
            """);
        FolderSession session = Open(folder);

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(session, ["MedicationRequest/m1"]);

        envelope.Facts.ShouldNotBeNull().ResolvedReferences.ShouldNotBeNull()["MedicationRequest/m1"]
            .ShouldBe(new Dictionary<string, string> { [Requester] = "Practitioner/pr1" });
        session.BuiltIndexTypes.ShouldBeEmpty();
    }

    // Regression (fix round 1): at f8b47276 two runs POSTing the same practitioner with ifNoneExist on its NPI made the
    // reference ambiguous, where a server creates only the first and resolves to it.
    [Fact]
    public void GivenDuplicatesTheLaterOfWhichIfNoneExistRulesOut_WhenIncluded_ThenTheFirstInCatalogOrderIsIncluded()
    {
        using TestFolder folder = Runs("""{"method":"POST","url":"Practitioner","ifNoneExist":"identifier=http://hl7.org/fhir/sid/us-npi|111"}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("MedicationRequest", "_include=MedicationRequest:requester"));

        Included(envelope).ShouldBe(["Practitioner/pr-run1"]);
        envelope.Issues.ShouldBeEmpty();
    }

    // Guard (fix round 1): a later duplicate is ruled out only by an ifNoneExist an earlier resource satisfies; with none, or
    // one naming another identifier, both would be created and the reference stays ambiguous. Mutation-checked: letting any
    // ifNoneExist rule an entry out, or resolving duplicates to the first, fails every row.
    [Theory]
    [InlineData("""{"method":"POST","url":"Practitioner"}""")]
    [InlineData("""{"method":"POST","url":"Practitioner","ifNoneExist":"identifier=http://hl7.org/fhir/sid/us-npi|222"}""")]
    [InlineData("""{"method":"POST","url":"Practitioner","ifNoneExist":"name=Smith"}""")]
    public void GivenDuplicatesNoIfNoneExistRulesOut_WhenIncluded_ThenTheReferenceIsAmbiguous(string secondRequest)
    {
        using TestFolder folder = Runs(secondRequest);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("MedicationRequest", "_include=MedicationRequest:requester"));

        Included(envelope).ShouldBeEmpty();
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldEndWith($": MedicationRequest/m1 -> {Requester} (conditional-ambiguous).");
    }

    // Regression (fix round 1): at f8b47276 a target file that could not be read threw from the lookup, and the cached
    // exception failed every later index build and read of the session.
    [Fact]
    public void GivenATargetFileRemovedAfterCataloguing_WhenSearchedAndRead_ThenTheReferenceIsReportedUnreadableAndNothingThrows()
    {
        using TestFolder folder = SyntheaShaped();
        FolderSession session = Open(folder);
        session.Catalog.Entries.ShouldNotBeEmpty();
        File.Delete(Path.Combine(folder.Path, "practitionerInformation.json"));

        ToolEnvelope<SearchFacts?> first = ResourceSearcher.Search(session, new SearchRequest("MedicationRequest", "_id=m1"));
        ToolEnvelope<ReadFacts?> read = ResourceFetcher.Read(session, ["MedicationRequest/m1"]);
        ToolEnvelope<SearchFacts?> other = ResourceSearcher.Search(session, new SearchRequest("Encounter", "_id=e1"));

        Ids(first).ShouldBe(["m1"]);
        first.Issues.ShouldContain(issue => issue.Code == UnresolvedCode
            && issue.Diagnostics.Contains("1 conditional-target-unreadable: a resource of that type could not be read", StringComparison.Ordinal)
            && issue.Diagnostics.EndsWith($": MedicationRequest/m1 -> {Requester} (conditional-target-unreadable).", StringComparison.Ordinal));
        read.Facts.ShouldNotBeNull().ResolvedReferences.ShouldNotBeNull()["MedicationRequest/m1"].ShouldNotContainKey(Requester);
        Ids(other).ShouldBe(["e1"]);
        other.Issues.ShouldContain(issue => issue.Code == UnresolvedCode && issue.Diagnostics.Contains("Encounter/e1 -> " + Provider + " (conditional-target-unreadable)", StringComparison.Ordinal));
    }

    // Regression (fix round 1): at f8b47276 the lookup read a changed file's range without checking it still held the
    // catalogued resource, so a stale range could credit or drop an identifier silently.
    [Fact]
    public void GivenATargetWhoseCataloguedRangeNoLongerHoldsIt_WhenSearched_ThenTheReferenceIsReportedUnreadable()
    {
        using TestFolder folder = SyntheaShaped();
        FolderSession session = Open(folder);
        session.Catalog.Entries.ShouldNotBeEmpty();
        string path = Path.Combine(folder.Path, "practitionerInformation.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"id\":\"pr1\"", "\"id\":\"pr9\"", StringComparison.Ordinal));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("MedicationRequest", "_id=m1"));

        envelope.Issues.ShouldContain(issue => issue.Code == UnresolvedCode
            && issue.Diagnostics.EndsWith($": MedicationRequest/m1 -> {Requester} (conditional-target-unreadable).", StringComparison.Ordinal));
    }

    // Regression (fix round 1): at f8b47276 the reference parser cut this to NamingSystem/npi under a base, so
    // references-unresolved counted it a second time beside the index's own issue.
    [Fact]
    public void GivenAnUnresolvedConditionalWhoseCriteriaHoldATypeAndId_WhenIncluded_ThenItIsReportedOnce()
    {
        const string reference = "Organization?identifier=https://x.org/fhir/NamingSystem/npi|1";
        using TestFolder folder = SyntheaShaped().Write("loose.ndjson", Encounter("e9", reference));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "_id=e9&_include=Encounter:service-provider"));

        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe(UnresolvedCode);
        issue.Diagnostics.ShouldEndWith($": Encounter/e9 -> {reference} (conditional-no-match).");
    }

    /// <summary>
    /// Two Synthea runs' practitioner Bundles, each POSTing a practitioner with NPI 111 under its own id (the first with
    /// ifNoneExist on the NPI, the second with <paramref name="secondRequest"/>), and a MedicationRequest referencing the NPI.
    /// </summary>
    private static TestFolder Runs(string secondRequest) => new TestFolder()
        .Write("run1-practitionerInformation.json", """
            {"resourceType":"Bundle","type":"batch","entry":[
              {"fullUrl":"urn:uuid:pr-run1","resource":{"resourceType":"Practitioner","id":"pr-run1","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"111"}]},
               "request":{"method":"POST","url":"Practitioner","ifNoneExist":"identifier=http://hl7.org/fhir/sid/us-npi|111"}}
            ]}
            """)
        .Write("run2-practitionerInformation.json", """
            {"resourceType":"Bundle","type":"batch","entry":[
              {"fullUrl":"urn:uuid:pr-run2","resource":{"resourceType":"Practitioner","id":"pr-run2","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"111"}]},
               "request":SECOND}
            ]}
            """.Replace("SECOND", secondRequest, StringComparison.Ordinal))
        .Write("run2-patient.ndjson", """
            {"resourceType":"MedicationRequest","id":"m1","status":"active","intent":"order","medicationCodeableConcept":{"text":"x"},"subject":{"reference":"Patient/p1"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}}
            """);

    private static string Encounter(string id, string serviceProvider) =>
        "{\"resourceType\":\"Encounter\",\"id\":\"" + id + "\",\"status\":\"finished\",\"class\":{\"code\":\"AMB\"},\"serviceProvider\":{\"reference\":\"" + serviceProvider + "\"}}";

    private static string[] Included(ToolEnvelope<SearchFacts?> envelope) =>
        [.. (envelope.Facts.ShouldNotBeNull().Included ?? []).Select(resource => $"{resource["resourceType"]}/{resource["id"]}")];

    private static FolderSession OpenCached(TestFolder folder, TestFolder cache) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cache.Path));
}
