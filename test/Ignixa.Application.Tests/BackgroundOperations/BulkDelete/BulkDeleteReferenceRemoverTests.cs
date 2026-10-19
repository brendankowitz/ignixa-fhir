// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Search.Indexing.SearchValues;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public class BulkDeleteReferenceRemoverTests
{
    private const string ServiceBase = "https://fhir.example.org/";

    /// <summary>
    /// The reference forms the search index collapses onto the plain relative reference, and so the forms
    /// <c>_revinclude=*:*</c> reports as referring to the target. Removal has to recognize every one of
    /// them: a referrer the cascade finds but removal does not rewrite keeps a reference to a resource that
    /// is about to be hard deleted.
    /// </summary>
    public static TheoryData<string> EquivalentReferenceForms() =>
    [
        "Patient/p1",
        "Patient/p1/_history/2",
        $"{ServiceBase}Patient/p1",
        $"{ServiceBase}Patient/p1/_history/2",
    ];

    private static IReferenceSearchValueParser CreateParser()
    {
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        schemaProvider.ResourceTypeNames.Returns(
            new HashSet<string> { "Patient", "Observation", "Practitioner", "Organization" });
        return new ReferenceSearchValueParser(schemaProvider, new StubBaseUriProvider(ServiceBase));
    }

    /// <summary>
    /// Inherits <see cref="IFhirBaseUriProvider.IsServiceBaseUri"/> so the test exercises the real
    /// equivalence rule rather than a stubbed answer to it.
    /// </summary>
    private sealed class StubBaseUriProvider(string baseUri) : IFhirBaseUriProvider
    {
        public Uri? GetBaseUri() => new(baseUri);
    }

    [Fact]
    public void GivenANestedReference_WhenRemovingReferences_ThenReferenceIsRemovedAndDisplayIsSet()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "Patient/p1",
                "display": "Patient One"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        var subject = resource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
        subject["display"]!.GetValue<string>().ShouldBe(BulkDeleteReferenceRemover.RemovedReferenceDisplay);
    }

    [Fact]
    public void GivenAReferenceinArrayElement_WhenRemovingReferences_ThenReferenceinArrayIsRemoved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "performer": [
                { "reference": "Practitioner/pr1", "display": "Dr. Smith" },
                { "reference": "Patient/p1", "display": "Patient One" },
                { "reference": "Organization/org1" }
              ]
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        var performer = resource["performer"]!.AsArray();
        performer.Count.ShouldBe(3);

        // First element should be unchanged
        performer[0]!["reference"]!.GetValue<string>().ShouldBe("Practitioner/pr1");

        // Second element should have reference removed and display updated
        performer[1]!["reference"].ShouldBeNull();
        performer[1]!["display"]!.GetValue<string>().ShouldBe(BulkDeleteReferenceRemover.RemovedReferenceDisplay);

        // Third element should be unchanged
        performer[2]!["reference"]!.GetValue<string>().ShouldBe("Organization/org1");
    }

    [Fact]
    public void GivenACaseInsensitiveMatch_WhenRemovingReferences_ThenReferenceIsRemoved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "PATIENT/P1"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "patient/p1", CreateParser());

        result.ShouldBeTrue();
        var subject = resource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
    }

    [Fact]
    public void GivenANonMatchingReference_WhenRemovingReferences_ThenReferenceIsUntouchedAndReturnsFalse()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "Patient/p2",
                "display": "Patient Two"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeFalse();
        var subject = resource["subject"]!.AsObject();
        subject["reference"]!.GetValue<string>().ShouldBe("Patient/p2");
        subject["display"]!.GetValue<string>().ShouldBe("Patient Two");
    }

    [Fact]
    public void GivenAnAbsoluteUrl_WhenRemovingReferences_ThenAbsoluteUrlIsNotMatched()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "https://example.com/fhir/Patient/p1"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeFalse();
        var subject = resource["subject"]!.AsObject();
        subject["reference"]!.GetValue<string>().ShouldBe("https://example.com/fhir/Patient/p1");
    }

    [Fact]
    public void GivenMultipleOccurrences_WhenRemovingReferences_ThenAllOccurrencesAreRemoved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "Patient/p1"
              },
              "performer": [
                { "reference": "Patient/p1" },
                { "reference": "Practitioner/pr1" }
              ],
              "encounter": {
                "reference": "Patient/p1"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        resource["subject"]!.AsObject()["reference"].ShouldBeNull();
        resource["performer"]![0]!.AsObject()["reference"].ShouldBeNull();
        resource["performer"]![1]!.AsObject()["reference"]!.GetValue<string>().ShouldBe("Practitioner/pr1");
        resource["encounter"]!.AsObject()["reference"].ShouldBeNull();
    }

    [Fact]
    public void GivenAContainedResource_WhenRemovingReferences_ThenReferenceinContainedIsAlsoRemoved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "contained": [
                {
                  "resourceType": "Patient",
                  "id": "p1",
                  "generalPractitioner": [
                    { "reference": "Practitioner/pr1" },
                    { "reference": "Patient/p1" }
                  ]
                }
              ],
              "subject": {
                "reference": "Patient/p1"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();

        // Top-level reference should be removed
        resource["subject"]!.AsObject()["reference"].ShouldBeNull();

        // Contained resource reference should also be removed
        var contained = resource["contained"]![0]!.AsObject();
        var generalPractitioner = contained["generalPractitioner"]!.AsArray();
        generalPractitioner[0]!.AsObject()["reference"]!.GetValue<string>().ShouldBe("Practitioner/pr1");
        generalPractitioner[1]!.AsObject()["reference"].ShouldBeNull();
    }

    [Fact]
    public void GivenAnIdentifierSibling_WhenRemovingReferences_ThenIdentifierIsPreserved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": "Patient/p1",
                "type": "Patient",
                "identifier": {
                  "system": "http://example.com",
                  "value": "12345"
                }
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        var subject = resource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
        subject["type"]!.GetValue<string>().ShouldBe("Patient");
        subject["identifier"]!.ShouldNotBeNull();
        subject["identifier"]!.AsObject()["system"]!.GetValue<string>().ShouldBe("http://example.com");
        subject["identifier"]!.AsObject()["value"]!.GetValue<string>().ShouldBe("12345");
    }

    [Fact]
    public void GivenANullResource_WhenRemovingReferences_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(null!, "Patient/p1", CreateParser()));
    }

    [Fact]
    public void GivenANullTarget_WhenRemovingReferences_ThenThrowsArgumentNullException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentNullException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, null!, CreateParser()));
    }

    [Fact]
    public void GivenAWhitespaceTarget_WhenRemovingReferences_ThenThrowsArgumentException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, "   ", CreateParser()));
    }

    [Fact]
    public void GivenAnEmptyStringTarget_WhenRemovingReferences_ThenThrowsArgumentException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, string.Empty, CreateParser()));
    }

    [Fact]
    public void GivenAResourceWithoutReferences_WhenRemovingReferences_ThenReturnsFalseWithResourceUnchanged()
    {
        var resourceJson = """
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "status": "final"
            }
            """;
        var resource = JsonNode.Parse(resourceJson)!;
        var originalJson = resource.ToJsonString();

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeFalse();
        resource.ToJsonString().ShouldBe(originalJson);
    }

    [Fact]
    public void GivenADeeplyNestedReference_WhenRemovingReferences_ThenReferenceIsRemoved()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Bundle",
              "entry": [
                {
                  "resource": {
                    "resourceType": "Observation",
                    "subject": {
                      "reference": "Patient/p1"
                    }
                  }
                }
              ]
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        var entry = resource["entry"]![0]!.AsObject();
        var obsResource = entry["resource"]!.AsObject();
        var subject = obsResource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
    }

    /// <summary>
    /// The index side of the cascade/removal contract: every form in
    /// <see cref="EquivalentReferenceForms"/> is stored as the same (type, id) pair, which is why
    /// <c>_revinclude=*:*</c> on <c>Patient/p1</c> reports a referrer written in any of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(EquivalentReferenceForms))]
    public void GivenAnEquivalentReferenceForm_WhenIndexed_ThenItResolvesToTheTarget(string reference)
    {
        var parsed = CreateParser().Parse(reference);

        parsed.ResourceType.ShouldBe("Patient");
        parsed.ResourceId.ShouldBe("p1");
        parsed.BaseUri.ShouldBeNull("A reference that resolves to this server carries no base URI.");
    }

    /// <summary>
    /// The removal side of the same contract: every form the index resolved to the target is recognized,
    /// so no referrer the cascade returned is left pointing at a resource the job hard deletes.
    /// </summary>
    [Theory]
    [MemberData(nameof(EquivalentReferenceForms))]
    public void GivenAnEquivalentReferenceForm_WhenRemovingReferences_ThenReferenceIsRemoved(string reference)
    {
        var resource = JsonNode.Parse($$"""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": {{System.Text.Json.JsonSerializer.Serialize(reference)}},
                "display": "Patient One"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        var subject = resource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
        subject["display"]!.GetValue<string>().ShouldBe(BulkDeleteReferenceRemover.RemovedReferenceDisplay);
    }

    /// <summary>
    /// The other side of the same rule: a reference under a base URI that is <em>not</em> this server's
    /// names a different resource, the index stores it with that base attached, and the cascade does not
    /// report it. Removal must leave it alone even though the type and id match.
    /// </summary>
    [Theory]
    [InlineData("https://other.example.org/fhir/Patient/p1")]
    [InlineData("https://other.example.org/fhir/Patient/p1/_history/2")]
    [InlineData("urn:uuid:5bbb07f5-3f0a-4f77-b2ba-9bb1e7f3f3aa")]
    [InlineData("#contained-p1")]
    public void GivenAReferenceThatDoesNotResolveToThisServer_WhenRemovingReferences_ThenItIsUntouched(string reference)
    {
        var resource = JsonNode.Parse($$"""
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "subject": {
                "reference": {{System.Text.Json.JsonSerializer.Serialize(reference)}}
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeFalse();
        resource["subject"]!.AsObject()["reference"]!.GetValue<string>().ShouldBe(reference);
    }

    [Fact]
    public void GivenANullReferenceParser_WhenRemovingReferences_ThenThrowsArgumentNullException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentNullException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", null!));
    }

    [Fact]
    public void GivenAResourceTypeIdAtTopLevel_WhenRemovingReferences_ThenTopLevelIdIsNotTreatedAsReference()
    {
        var resource = JsonNode.Parse("""
            {
              "resourceType": "Patient",
              "id": "p1",
              "subject": {
                "reference": "Patient/p1"
              }
            }
            """)!;

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1", CreateParser());

        result.ShouldBeTrue();
        // The top-level "id" should still be "p1" (it's not a "reference" property)
        resource["id"]!.GetValue<string>().ShouldBe("p1");
        // But the nested reference should be removed
        resource["subject"]!.AsObject()["reference"].ShouldBeNull();
    }
}
