// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The folder index now uses Ignixa's shared converters unchanged, so the behavior the tool once got from its own
/// copies must come from them. Regressions (fail at 206143a): <c>telecom=phone|...</c> (the tool's copy indexed
/// <c>system|value</c>), R5 <c>_profile</c> (the stock indexer reported an <c>index-extraction-failed</c> error for the
/// canonical next to the tool's synthesized entry) and the <c>Special</c> composite rows (reported as unparseable input).
/// The KBV <c>_profile</c> rows and the include refusal fail at b096951, where canonicals indexed as references (the
/// two profiles collided on <c>StructureDefinition/KBV</c> and a version was ignored). The other rows are guards,
/// passing at 206143a through the tool's copies and mutation-checked against the shared fix. The participant and wildcard
/// rows fail at 78d8b45, where a canonical entry made the follow throw once it was reached or went uncounted.
/// </summary>
public class SharedIndexingTests
{
    private const string Patients = """
        {"resourceType":"Patient","id":"p1","meta":{"profile":["http://example.org/fhir/StructureDefinition/my-patient"]},"telecom":[{"system":"phone","value":"555-1234","use":"home"}]}
        {"resourceType":"Patient","id":"p2","telecom":[{"system":"email","value":"x@example.org","use":"work"}]}
        {"resourceType":"Patient","id":"kbv-patient","meta":{"profile":["https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient|1.5.0"]}}
        {"resourceType":"Patient","id":"kbv-observation","meta":{"profile":["https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Observation"]}}
        """;

    private const string Observations = """
        {"resourceType":"Observation","id":"w1","status":"final","code":{"text":"weight"},"subject":{"reference":"urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"},"valueQuantity":{"value":185,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"}}
        {"resourceType":"Observation","id":"w2","status":"final","code":{"text":"weight"},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":84,"unit":"kg","system":"http://unitsofmeasure.org","code":"kg"}}
        """;

    private const string Smith = """{"resourceType":"Patient","id":"p1","name":[{"family":"Smith"}]}""";

    private const string Orchestrations = """
        {"resourceType":"RequestOrchestration","id":"ro1","status":"active","intent":"plan","action":[{"participant":[{"actorReference":{"reference":"Patient/p1"}},{"actorCanonical":"http://example.org/fhir/PlanDefinition/pd"}]}]}
        {"resourceType":"RequestOrchestration","id":"ro2","status":"active","intent":"plan","action":[{"participant":[{"actorReference":{"reference":"Patient/p1"}}]}]}
        """;

    private const string UsCorePatient = """{"resourceType":"Patient","id":"p1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient|7.0.0"]},"name":[{"family":"Smith"}]}""";

    private const string UsCoreObservation = """{"resourceType":"Observation","id":"o1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab"]},"status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1"}}""";

    [Theory]
    [InlineData(FhirVersion.R4, "Patient", "telecom=555-1234", "p1")]
    [InlineData(FhirVersion.R4, "Patient", "telecom=home|555-1234", "")]
    [InlineData(FhirVersion.R4, "Patient", "telecom=phone|555-1234", "")]
    [InlineData(FhirVersion.R4, "Observation", "value-quantity=185||lbs", "w1")]
    [InlineData(FhirVersion.R4, "Observation", "subject=urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d", "w1")]
    [InlineData(FhirVersion.R4, "Observation", "subject:missing=true", "")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=http://example.org/fhir/StructureDefinition/my-patient", "p1")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient", "kbv-patient")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient|1.5.0", "kbv-patient")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient|1.4.0", "")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Observation", "kbv-observation")]
    public void GivenAFolder_WhenSearchedOnValuesTheSharedConvertersIndex_ThenTheSpecifiedResourcesMatch(FhirVersion version, string resourceType, string query, string expected)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patients).Write("observations.ndjson", Observations);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, version), new SearchRequest(resourceType, query));

        envelope.Issues.Where(issue => issue.Severity == "error").ShouldBeEmpty();
        Ids(envelope).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
    }

    [Fact]
    public void GivenAnIncludeThroughACanonicalParameter_WhenSearched_ThenTheCallFailsAsNotSupported()
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patients);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, FhirVersion.R5), new SearchRequest("Patient", "_include=Patient:_profile"));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe(SearchFailures.NotSupportedCode);
        issue.Diagnostics.ShouldNotBeNull().ShouldContain("canonical resolution is not supported");
    }

    [Theory]
    [InlineData("RequestOrchestration", "participant:Patient.name=Smith", "ro1,ro2")]
    [InlineData("RequestOrchestration", "participant:Patient.name=Jones", "")]
    [InlineData("RequestOrchestration", "_id=ro1&_include=RequestOrchestration:participant", "ro1,p1")]
    [InlineData("Patient", "_revinclude=RequestOrchestration:participant", "p1,ro1,ro2")]
    public void GivenAParameterOverReferencesAndCanonicals_WhenFollowed_ThenItsReferencesAreFollowedAndItsCanonicalIsCounted(string resourceType, string query, string expected)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Smith).Write("orchestrations.ndjson", Orchestrations);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, FhirVersion.R5), new SearchRequest(resourceType, query));

        Returned(envelope).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        (issue.Severity, issue.Code).ShouldBe(("information", ReferenceFollowChecks.UnresolvedCode));
        issue.Diagnostics.ShouldStartWith("1 references through RequestOrchestration:participant are not local 'Type/id' references (canonicals,");
    }

    [Theory]
    [InlineData("_include=*", "2 references through *:*")]
    [InlineData("_include=Observation:*", "1 references through Observation:*")]
    public void GivenProfiledResources_WhenIncludedByWildcard_ThenTheReferencesAreFollowedAndTheProfilesCounted(string query, string unresolved)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", UsCorePatient).Write("observations.ndjson", UsCoreObservation);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, FhirVersion.R5), new SearchRequest("Observation", query));

        Returned(envelope).ShouldBe(["o1", "p1"]);
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        (issue.Severity, issue.Code).ShouldBe(("information", ReferenceFollowChecks.UnresolvedCode));
        issue.Diagnostics.ShouldStartWith(unresolved);
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenACompositeWithASpecialComponent_WhenSearched_ThenTheCallFailsAsNotSupportedNamingTheParameter(FhirVersion version)
    {
        using TestFolder folder = new TestFolder().Write("composition.json", """{"resourceType":"Composition","id":"c1","status":"final","type":{"text":"x"},"date":"2020-01-01","author":[{"display":"a"}],"title":"t"}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, version), new SearchRequest("Composition", "section-code-text=10154-3$chief"));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe(SearchFailures.NotSupportedCode);
        issue.Diagnostics.ShouldNotBeNull().ShouldContain("section-code-text");
        issue.Diagnostics.ShouldContain("Special");
    }

    /// <summary>The ids of the matches, then of the included resources, in the order returned.</summary>
    private static string[] Returned(ToolEnvelope<SearchFacts?> envelope) =>
        [.. Ids(envelope), .. (envelope.Facts.ShouldNotBeNull().Included ?? []).Select(resource => (string)resource["id"]!)];

    private static FolderSession Open(TestFolder folder, FhirVersion version) => new(
        folder.Path,
        version switch
        {
            FhirVersion.R4 => new R4CoreSchemaProvider(),
            FhirVersion.R5 => new R5CoreSchemaProvider(),
            FhirVersion.R6 => new R6CoreSchemaProvider(),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
        },
        NullLoggerFactory.Instance);
}
