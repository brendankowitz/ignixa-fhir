// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public class BulkDeleteReferenceRemoverTests
{
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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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
            BulkDeleteReferenceRemover.RemoveReferences(null!, "Patient/p1"));
    }

    [Fact]
    public void GivenANullTarget_WhenRemovingReferences_ThenThrowsArgumentNullException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentNullException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, null!));
    }

    [Fact]
    public void GivenAWhitespaceTarget_WhenRemovingReferences_ThenThrowsArgumentException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, "   "));
    }

    [Fact]
    public void GivenAnEmptyStringTarget_WhenRemovingReferences_ThenThrowsArgumentException()
    {
        var resource = JsonNode.Parse("""{"resourceType":"Observation","id":"obs-1"}""")!;

        Should.Throw<ArgumentException>(() =>
            BulkDeleteReferenceRemover.RemoveReferences(resource, string.Empty));
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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

        result.ShouldBeTrue();
        var entry = resource["entry"]![0]!.AsObject();
        var obsResource = entry["resource"]!.AsObject();
        var subject = obsResource["subject"]!.AsObject();
        subject["reference"].ShouldBeNull();
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

        var result = BulkDeleteReferenceRemover.RemoveReferences(resource, "Patient/p1");

        result.ShouldBeTrue();
        // The top-level "id" should still be "p1" (it's not a "reference" property)
        resource["id"]!.GetValue<string>().ShouldBe("p1");
        // But the nested reference should be removed
        resource["subject"]!.AsObject()["reference"].ShouldBeNull();
    }
}
