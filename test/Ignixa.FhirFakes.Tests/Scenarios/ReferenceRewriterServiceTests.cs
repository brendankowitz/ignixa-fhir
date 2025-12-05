// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for the ReferenceRewriterService class.
/// Validates reference rewriting between urn:uuid and resolved formats.
/// </summary>
public class ReferenceRewriterServiceTests
{
    private readonly IReferenceMetadataProvider _metadataProvider = new R4ReferenceMetadata();

    #region Constructor Tests

    [Fact]
    public void GivenNullMetadataProvider_WhenCreatingService_ThenThrowsArgumentNullException()
    {
        // Act
        var act = () => new ReferenceRewriterService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("metadataProvider");
    }

    [Fact]
    public void GivenValidMetadataProvider_WhenCreatingService_ThenSucceeds()
    {
        // Act
        var act = () => new ReferenceRewriterService(_metadataProvider);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region RewriteReferences Null Parameter Tests

    [Fact]
    public void GivenNullResources_WhenRewritingReferences_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var identities = new Dictionary<string, ResourceIdentity>();

        // Act
        var act = () => service.RewriteReferences(null!, identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resources");
    }

    [Fact]
    public void GivenNullIdentities_WhenRewritingReferences_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var resources = Array.Empty<ResourceJsonNode>();

        // Act
        var act = () => service.RewriteReferences(resources, null!, ReferenceFormat.Resolved);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("identities");
    }

    #endregion

    #region Simple Reference Rewriting Tests

    [Fact]
    public void GivenObservationWithUrnUuidSubjectReference_WhenRewritingToResolved_ThenReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();

        var observation = CreateObservationWithSubjectReference($"urn:uuid:{patientId}");
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{patientId}");
    }

    [Fact]
    public void GivenObservationWithResolvedSubjectReference_WhenRewritingToUrnUuid_ThenReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();

        var observation = CreateObservationWithSubjectReference($"Patient/{patientId}");
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.UrnUuid);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"urn:uuid:{patientId}");
    }

    [Fact]
    public void GivenObservationWithEncounterReference_WhenRewritingToResolved_ThenBothReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var encounterId = Guid.NewGuid().ToString();

        var observation = CreateObservationWithSubjectAndEncounter(
            $"urn:uuid:{patientId}",
            $"urn:uuid:{encounterId}");

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId),
            [encounterId] = new ResourceIdentity("Encounter", encounterId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{patientId}");
        encounterRef.Should().Be($"Encounter/{encounterId}");
    }

    #endregion

    #region Fragment Reference Tests

    [Fact]
    public void GivenObservationWithFragmentReference_WhenRewriting_ThenFragmentReferenceIsSkipped()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();

        // Create an observation with a contained fragment reference
        var observation = CreateObservationWithSubjectReference("#contained-patient");
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be("#contained-patient");
    }

    [Fact]
    public void GivenObservationWithMixedReferences_WhenRewriting_ThenOnlyNonFragmentReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var encounterId = Guid.NewGuid().ToString();

        // Create observation with fragment subject and urn:uuid encounter
        var observation = CreateObservationWithSubjectAndEncounter(
            "#contained-patient",
            $"urn:uuid:{encounterId}");

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [encounterId] = new ResourceIdentity("Encounter", encounterId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be("#contained-patient"); // Fragment preserved
        encounterRef.Should().Be($"Encounter/{encounterId}"); // Rewritten
    }

    #endregion

    #region External Reference Tests

    [Fact]
    public void GivenObservationWithExternalReference_WhenRewriting_ThenExternalReferenceIsSkipped()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var unknownId = Guid.NewGuid().ToString();

        // Create observation referencing a patient not in the registry
        var observation = CreateObservationWithSubjectReference($"urn:uuid:{unknownId}");
        var identities = new Dictionary<string, ResourceIdentity>(); // Empty registry

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"urn:uuid:{unknownId}"); // Unchanged
    }

    [Fact]
    public void GivenObservationWithPartiallyKnownReferences_WhenRewriting_ThenOnlyKnownReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var unknownEncounterId = Guid.NewGuid().ToString();

        var observation = CreateObservationWithSubjectAndEncounter(
            $"urn:uuid:{patientId}",
            $"urn:uuid:{unknownEncounterId}");

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
            // Encounter not in registry
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{patientId}"); // Rewritten
        encounterRef.Should().Be($"urn:uuid:{unknownEncounterId}"); // Unchanged
    }

    #endregion

    #region Versioned Reference Tests

    [Fact]
    public void GivenObservationWithVersionedReference_WhenRewriting_ThenExtratesIdCorrectly()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();

        var observation = CreateObservationWithSubjectReference($"Patient/{patientId}/_history/2");
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.UrnUuid);

        // Assert
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"urn:uuid:{patientId}");
    }

    #endregion

    #region Absolute URL Reference Tests

    [Fact]
    public void GivenObservationWithAbsoluteUrlReference_WhenRewriting_ThenExternalReferenceIsUnchanged()
    {
        // Arrange
        // Absolute URLs are not currently rewritten - they are treated as external references
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var absoluteUrl = $"http://example.com/fhir/Patient/{patientId}";

        var observation = CreateObservationWithSubjectReference(absoluteUrl);
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.UrnUuid);

        // Assert - External URL references are preserved unchanged
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be(absoluteUrl);
    }

    [Fact]
    public void GivenObservationWithHttpsUrlReference_WhenRewriting_ThenExternalReferenceIsUnchanged()
    {
        // Arrange
        // Absolute URLs are not currently rewritten - they are treated as external references
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var absoluteUrl = $"https://example.com/fhir/Patient/{patientId}";

        var observation = CreateObservationWithSubjectReference(absoluteUrl);
        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert - External URL references are preserved unchanged
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be(absoluteUrl);
    }

    #endregion

    #region Resource With No Reference Metadata Tests

    [Fact]
    public void GivenResourceWithNoReferenceMetadata_WhenRewriting_ThenNoChangesAreMade()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();

        // Patient has no references to other resources (at the top level)
        var patient = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Patient",
                "id": "{{patientId}}",
                "active": true
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        var act = () => service.RewriteReferences([patient], identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().NotThrow();
        patient.MutableNode["resourceType"]?.GetValue<string>().Should().Be("Patient");
        patient.MutableNode["id"]?.GetValue<string>().Should().Be(patientId);
    }

    #endregion

    #region Missing Reference Field Tests

    [Fact]
    public void GivenObservationWithoutSubjectField_WhenRewriting_ThenNoExceptionIsThrown()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var id = Guid.NewGuid().ToString();

        var observation = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final"
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>();

        // Act
        var act = () => service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenObservationWithNullReferenceValue_WhenRewriting_ThenNoExceptionIsThrown()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var id = Guid.NewGuid().ToString();

        var observation = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {}
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>();

        // Act
        var act = () => service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenObservationWithEmptyReferenceValue_WhenRewriting_ThenNoChangesAreMade()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var id = Guid.NewGuid().ToString();

        var observation = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {
                    "reference": ""
                }
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>();

        // Act
        var act = () => service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().NotThrow();
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be("");
    }

    #endregion

    #region Reference Display Text Preservation Tests

    [Fact]
    public void GivenObservationWithReferenceDisplay_WhenRewriting_ThenDisplayTextIsPreserved()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var id = Guid.NewGuid().ToString();

        var observation = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {
                    "reference": "urn:uuid:{{patientId}}",
                    "display": "John Smith"
                }
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subject = observation.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        subject?["display"]?.GetValue<string>().Should().Be("John Smith");
    }

    [Fact]
    public void GivenObservationWithReferenceTypeAndIdentifier_WhenRewriting_ThenAdditionalFieldsArePreserved()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var id = Guid.NewGuid().ToString();

        var observation = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {
                    "reference": "urn:uuid:{{patientId}}",
                    "type": "Patient",
                    "identifier": {
                        "system": "http://example.org/mrn",
                        "value": "12345"
                    }
                }
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId)
        };

        // Act
        service.RewriteReferences([observation], identities, ReferenceFormat.Resolved);

        // Assert
        var subject = observation.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        subject?["type"]?.GetValue<string>().Should().Be("Patient");
        subject?["identifier"]?["value"]?.GetValue<string>().Should().Be("12345");
    }

    #endregion

    #region Reference Array Tests

    [Fact]
    public void GivenConditionWithReasonReferenceArray_WhenRewriting_ThenAllReferencesInArrayAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var obsId1 = Guid.NewGuid().ToString();
        var obsId2 = Guid.NewGuid().ToString();
        var id = Guid.NewGuid().ToString();

        var procedure = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Procedure",
                "id": "{{id}}",
                "status": "completed",
                "reasonReference": [
                    { "reference": "urn:uuid:{{obsId1}}" },
                    { "reference": "urn:uuid:{{obsId2}}" }
                ]
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [obsId1] = new ResourceIdentity("Observation", obsId1),
            [obsId2] = new ResourceIdentity("Observation", obsId2)
        };

        // Act
        service.RewriteReferences([procedure], identities, ReferenceFormat.Resolved);

        // Assert
        var reasonReferences = procedure.MutableNode["reasonReference"]?.AsArray();
        reasonReferences.Should().HaveCount(2);
        reasonReferences?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obsId1}");
        reasonReferences?[1]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obsId2}");
    }

    [Fact]
    public void GivenConditionWithMixedReasonReferenceArray_WhenRewriting_ThenOnlyKnownReferencesAreRewritten()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var obsId1 = Guid.NewGuid().ToString();
        var unknownId = Guid.NewGuid().ToString();
        var id = Guid.NewGuid().ToString();

        var procedure = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Procedure",
                "id": "{{id}}",
                "status": "completed",
                "reasonReference": [
                    { "reference": "urn:uuid:{{obsId1}}" },
                    { "reference": "urn:uuid:{{unknownId}}" }
                ]
            }
            """);

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [obsId1] = new ResourceIdentity("Observation", obsId1)
            // unknownId not in registry
        };

        // Act
        service.RewriteReferences([procedure], identities, ReferenceFormat.Resolved);

        // Assert
        var reasonReferences = procedure.MutableNode["reasonReference"]?.AsArray();
        reasonReferences?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obsId1}");
        reasonReferences?[1]?["reference"]?.GetValue<string>().Should().Be($"urn:uuid:{unknownId}");
    }

    #endregion

    #region Multiple Resources Tests

    [Fact]
    public void GivenMultipleResources_WhenRewriting_ThenAllResourcesAreProcessed()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var patientId = Guid.NewGuid().ToString();
        var encounterId = Guid.NewGuid().ToString();

        var observation1 = CreateObservationWithSubjectReference($"urn:uuid:{patientId}");
        var observation2 = CreateObservationWithSubjectReference($"urn:uuid:{patientId}");
        var encounter = CreateEncounterWithSubjectReference($"urn:uuid:{patientId}");

        var identities = new Dictionary<string, ResourceIdentity>
        {
            [patientId] = new ResourceIdentity("Patient", patientId),
            [encounterId] = new ResourceIdentity("Encounter", encounterId)
        };

        // Act
        service.RewriteReferences([observation1, observation2, encounter], identities, ReferenceFormat.Resolved);

        // Assert
        observation1.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        observation2.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        encounter.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
    }

    [Fact]
    public void GivenEmptyResourceList_WhenRewriting_ThenNoExceptionIsThrown()
    {
        // Arrange
        var service = new ReferenceRewriterService(_metadataProvider);
        var resources = Array.Empty<ResourceJsonNode>();
        var identities = new Dictionary<string, ResourceIdentity>();

        // Act
        var act = () => service.RewriteReferences(resources, identities, ReferenceFormat.Resolved);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Helper Methods

    private static ResourceJsonNode CreateObservationWithSubjectReference(string subjectReference)
    {
        var id = Guid.NewGuid().ToString();
        var json = $$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {
                    "reference": "{{subjectReference}}"
                }
            }
            """;
        return ResourceJsonNode.Parse(json);
    }

    private static ResourceJsonNode CreateObservationWithSubjectAndEncounter(string subjectReference, string encounterReference)
    {
        var id = Guid.NewGuid().ToString();
        var json = $$"""
            {
                "resourceType": "Observation",
                "id": "{{id}}",
                "status": "final",
                "subject": {
                    "reference": "{{subjectReference}}"
                },
                "encounter": {
                    "reference": "{{encounterReference}}"
                }
            }
            """;
        return ResourceJsonNode.Parse(json);
    }

    private static ResourceJsonNode CreateEncounterWithSubjectReference(string subjectReference)
    {
        var id = Guid.NewGuid().ToString();
        var json = $$"""
            {
                "resourceType": "Encounter",
                "id": "{{id}}",
                "status": "finished",
                "class": {
                    "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                    "code": "AMB"
                },
                "subject": {
                    "reference": "{{subjectReference}}"
                }
            }
            """;
        return ResourceJsonNode.Parse(json);
    }

    #endregion
}
