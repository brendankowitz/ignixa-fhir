// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure.Harness;
using Ignixa.Api.E2ETests._TestData.Scenarios;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._Infrastructure.Base;

/// <summary>
/// Base class for include search tests with shared helper methods and scenario setup.
/// </summary>
public abstract class IncludeTestBase : CapabilityDrivenTestBase
{
    protected IncludeTestBase(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Helper Methods

    /// <summary>
    /// Creates include test data and uploads all resources to the server.
    /// </summary>
    protected async Task<IncludeTestData> CreateIncludeTestDataAsync(string tag)
    {
        var data = SchemaProvider.GetIncludeSearchScenario(tag);
        await Harness.CreateResourcesAsync(data.AllResources.ToArray());
        return data;
    }

    #region JSON Construction Helpers

    /// <summary>
    /// Creates a FHIR Reference JSON object.
    /// </summary>
    protected static JsonObject CreateReferenceJson(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    /// <summary>
    /// Creates a CodeableConcept JSON object with the specified system and code.
    /// </summary>
    protected static JsonObject CreateCodeableConceptJson(string system, string code)
    {
        return new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = system,
                    ["code"] = code
                }
            }
        };
    }

    /// <summary>
    /// Creates a meta tag JSON object for test isolation.
    /// </summary>
    protected static JsonObject CreateMetaTagJson(string tag)
    {
        return new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "testTag",
                    ["code"] = tag
                }
            }
        };
    }

    #endregion

    /// <summary>
    /// Creates an Organization resource with a tag using the fluent OrganizationBuilder.
    /// </summary>
    protected ResourceJsonNode CreateOrganizationResource(string tag, string? name = null, string? partOfId = null)
    {
        var builder = CreateOrganization()
            .WithTag(tag);

        if (name is not null)
        {
            builder = builder.WithName(name);
        }

        if (partOfId is not null)
        {
            builder = builder.WithPartOf(partOfId);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a Location resource with a tag and optional organization reference.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateLocation(string tag, string? managingOrgId = null, string? partOfId = null)
    {
        var location = new ResourceJsonNode
        {
            ResourceType = "Location",
            Id = Guid.NewGuid().ToString()
        };
        location.MutableNode["meta"] = CreateMetaTagJson(tag);

        if (managingOrgId is not null)
        {
            location.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", managingOrgId);
        }
        if (partOfId is not null)
        {
            location.MutableNode["partOf"] = CreateReferenceJson("Location", partOfId);
        }
        return location;
    }

    /// <summary>
    /// Creates a Practitioner resource with a tag.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreatePractitioner(string tag, string? familyName = null)
    {
        var practitioner = new ResourceJsonNode
        {
            ResourceType = "Practitioner",
            Id = Guid.NewGuid().ToString()
        };
        practitioner.MutableNode["meta"] = CreateMetaTagJson(tag);

        if (familyName is not null)
        {
            practitioner.MutableNode["name"] = new JsonArray
            {
                new JsonObject
                {
                    ["family"] = familyName
                }
            };
        }
        return practitioner;
    }

    /// <summary>
    /// Creates a Patient resource with a tag and optional references.
    /// Uses fluent PatientBuilder for core patient properties.
    /// </summary>
    protected ResourceJsonNode CreatePatientWithReferences(
        string tag,
        string familyName,
        string? birthDate = null,
        string? generalPractitionerId = null,
        string? managingOrganizationId = null)
    {
        var builder = CreatePatient()
            .FromSeattle()
            .WithFamilyName(familyName)
            .WithTag(tag);

        if (generalPractitionerId is not null)
        {
            builder = builder.WithGeneralPractitioner(generalPractitionerId);
        }

        if (managingOrganizationId is not null)
        {
            builder = builder.WithManagingOrganization(managingOrganizationId);
        }

        var patient = builder.Build();

        // birthDate override - PatientBuilder calculates from age
        if (birthDate is not null)
        {
            patient.MutableNode["birthDate"] = birthDate;
        }

        return patient;
    }

    /// <summary>
    /// Creates an Observation resource with a tag and references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateObservation(
        string tag,
        string patientId,
        string code,
        string codeSystem,
        string? practitionerId = null,
        string? organizationId = null,
        bool untypedReferences = false)
    {
        var obs = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = Guid.NewGuid().ToString()
        };
        obs.MutableNode["meta"] = CreateMetaTagJson(tag);
        obs.MutableNode["status"] = "final";
        obs.MutableNode["code"] = CreateCodeableConceptJson(codeSystem, code);

        // Handle untyped references for specific test cases
        obs.MutableNode["subject"] = untypedReferences
            ? new JsonObject { ["reference"] = patientId }
            : CreateReferenceJson("Patient", patientId);

        if (practitionerId is not null || organizationId is not null)
        {
            var performers = new JsonArray();
            if (organizationId is not null)
            {
                performers.Add(untypedReferences
                    ? new JsonObject { ["reference"] = organizationId }
                    : CreateReferenceJson("Organization", organizationId));
            }
            if (practitionerId is not null)
            {
                performers.Add(untypedReferences
                    ? new JsonObject { ["reference"] = practitionerId }
                    : CreateReferenceJson("Practitioner", practitionerId));
            }
            obs.MutableNode["performer"] = performers;
        }

        return obs;
    }

    /// <summary>
    /// Creates a DiagnosticReport resource with a tag and references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateDiagnosticReport(
        string tag,
        string patientId,
        string code,
        string codeSystem,
        string? observationId = null)
    {
        var report = new ResourceJsonNode
        {
            ResourceType = "DiagnosticReport",
            Id = Guid.NewGuid().ToString()
        };
        report.MutableNode["meta"] = CreateMetaTagJson(tag);
        report.MutableNode["status"] = "final";
        report.MutableNode["code"] = CreateCodeableConceptJson(codeSystem, code);
        report.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);

        if (observationId is not null)
        {
            report.MutableNode["result"] = new JsonArray
            {
                CreateReferenceJson("Observation", observationId)
            };
        }

        return report;
    }

    /// <summary>
    /// Creates a Group resource with member references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateGroup(string tag, params string[] patientIds)
    {
        var group = new ResourceJsonNode
        {
            ResourceType = "Group",
            Id = Guid.NewGuid().ToString()
        };
        group.MutableNode["meta"] = CreateMetaTagJson(tag);
        group.MutableNode["type"] = "person";
        group.MutableNode["actual"] = true;
        group.MutableNode["member"] = CreateGroupMemberArray(patientIds);

        return group;
    }

    /// <summary>
    /// Creates a Group.member array with Patient references.
    /// </summary>
    protected static JsonArray CreateGroupMemberArray(params string[] patientIds)
    {
        var members = new JsonArray();
        foreach (var id in patientIds)
        {
            members.Add(new JsonObject
            {
                ["entity"] = CreateReferenceJson("Patient", id)
            });
        }
        return members;
    }

    /// <summary>
    /// Creates a CareTeam resource with participant references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateCareTeam(string tag, string[] patientIds, string? organizationId = null, string? practitionerId = null)
    {
        var careTeam = new ResourceJsonNode
        {
            ResourceType = "CareTeam",
            Id = Guid.NewGuid().ToString()
        };
        careTeam.MutableNode["meta"] = CreateMetaTagJson(tag);
        careTeam.MutableNode["participant"] = CreateCareTeamParticipantArray(patientIds, organizationId, practitionerId);

        return careTeam;
    }

    /// <summary>
    /// Creates a CareTeam.participant array with member references.
    /// </summary>
    protected static JsonArray CreateCareTeamParticipantArray(string[] patientIds, string? organizationId, string? practitionerId)
    {
        var participants = new JsonArray();

        foreach (var patientId in patientIds)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Patient", patientId)
            });
        }
        if (organizationId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Organization", organizationId)
            });
        }
        if (practitionerId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Practitioner", practitionerId)
            });
        }

        return participants;
    }

    /// <summary>
    /// Validates that a bundle contains resources with the expected IDs.
    /// </summary>
    protected void ValidateBundleContains(BundleJsonNode bundle, params string[] expectedIds)
    {
        var actualIds = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.Id)
            .ToHashSet();

        foreach (var expectedId in expectedIds)
        {
            actualIds.ShouldContain(expectedId, $"bundle should contain resource with ID {expectedId}");
        }
    }

    /// <summary>
    /// Validates the search entry modes in a bundle.
    /// Match resources should have mode "match", included resources should have mode "include".
    /// </summary>
    protected void ValidateSearchEntryMode(BundleJsonNode bundle, string matchResourceType)
    {
        foreach (var entry in bundle.Entry)
        {
            if (entry.Resource is null) continue;

            var expectedMode = entry.Resource.ResourceType == matchResourceType ? "match" : "include";
            entry.Search?.Mode.ShouldBe(expectedMode,
                $"Resource {entry.Resource.ResourceType}/{entry.Resource.Id} should have search mode {expectedMode}");
        }
    }

    /// <summary>
    /// Gets the count of resources with a specific search mode.
    /// </summary>
    protected int GetCountBySearchMode(BundleJsonNode bundle, string mode)
    {
        return bundle.Entry.Count(e => e.Search?.Mode == mode);
    }

    /// <summary>
    /// Creates a MedicationRequest resource with a tag and patient reference.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateMedicationRequest(string tag, string patientId)
    {
        var medRequest = new ResourceJsonNode
        {
            ResourceType = "MedicationRequest",
            Id = Guid.NewGuid().ToString()
        };
        medRequest.MutableNode["meta"] = CreateMetaTagJson(tag);
        medRequest.MutableNode["status"] = "completed";
        medRequest.MutableNode["intent"] = "order";
        medRequest.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);
        medRequest.MutableNode["medicationCodeableConcept"] = CreateCodeableConceptJson("http://snomed.info/sct", "16590-619-30");

        return medRequest;
    }

    /// <summary>
    /// Creates a MedicationDispense resource with a tag, patient reference, and optional whenPrepared date.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    protected ResourceJsonNode CreateMedicationDispense(string tag, string patientId, string? whenPrepared, string? medicationRequestId)
    {
        var medDispense = new ResourceJsonNode
        {
            ResourceType = "MedicationDispense",
            Id = Guid.NewGuid().ToString()
        };
        medDispense.MutableNode["meta"] = CreateMetaTagJson(tag);
        medDispense.MutableNode["status"] = "in-progress";
        medDispense.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);
        medDispense.MutableNode["medicationCodeableConcept"] = CreateCodeableConceptJson("http://snomed.info/sct", "108505002");

        if (whenPrepared is not null)
        {
            medDispense.MutableNode["whenPrepared"] = whenPrepared;
        }

        if (medicationRequestId is not null)
        {
            medDispense.MutableNode["authorizingPrescription"] = new JsonArray
            {
                CreateReferenceJson("MedicationRequest", medicationRequestId)
            };
        }

        return medDispense;
    }

    #endregion
}
