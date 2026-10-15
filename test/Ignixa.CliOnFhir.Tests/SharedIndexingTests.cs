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
/// The other rows are guards, passing at 206143a through the tool's copies and mutation-checked against the shared fix.
/// </summary>
public class SharedIndexingTests
{
    private const string Patients = """
        {"resourceType":"Patient","id":"p1","meta":{"profile":["http://example.org/fhir/StructureDefinition/my-patient"]},"telecom":[{"system":"phone","value":"555-1234","use":"home"}]}
        {"resourceType":"Patient","id":"p2","telecom":[{"system":"email","value":"x@example.org","use":"work"}]}
        """;

    private const string Observations = """
        {"resourceType":"Observation","id":"w1","status":"final","code":{"text":"weight"},"subject":{"reference":"urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"},"valueQuantity":{"value":185,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"}}
        {"resourceType":"Observation","id":"w2","status":"final","code":{"text":"weight"},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":84,"unit":"kg","system":"http://unitsofmeasure.org","code":"kg"}}
        """;

    [Theory]
    [InlineData(FhirVersion.R4, "Patient", "telecom=555-1234", "p1")]
    [InlineData(FhirVersion.R4, "Patient", "telecom=home|555-1234", "")]
    [InlineData(FhirVersion.R4, "Patient", "telecom=phone|555-1234", "")]
    [InlineData(FhirVersion.R4, "Observation", "value-quantity=185||lbs", "w1")]
    [InlineData(FhirVersion.R4, "Observation", "subject=urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d", "w1")]
    [InlineData(FhirVersion.R4, "Observation", "subject:missing=true", "")]
    [InlineData(FhirVersion.R5, "Patient", "_profile=http://example.org/fhir/StructureDefinition/my-patient", "p1")]
    public void GivenAFolder_WhenSearchedOnValuesTheSharedConvertersIndex_ThenTheSpecifiedResourcesMatch(FhirVersion version, string resourceType, string query, string expected)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patients).Write("observations.ndjson", Observations);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, version), new SearchRequest(resourceType, query));

        envelope.Issues.Where(issue => issue.Severity == "error").ShouldBeEmpty();
        Ids(envelope).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
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
