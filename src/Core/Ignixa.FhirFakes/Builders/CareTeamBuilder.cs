// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating CareTeam resources.
/// Provides clean API for creating care teams with participants and roles.
/// </summary>
/// <remarks>
/// <para>
/// CareTeam resources coordinate care across multiple practitioners and organizations.
/// This builder supports:
/// </para>
/// <list type="bullet">
/// <item><description>Team status (active, suspended, inactive, etc.)</description></item>
/// <item><description>Team name and subject (patient) reference</description></item>
/// <item><description>Multiple participants with optional roles</description></item>
/// <item><description>Convenience methods for common participant types (Patient, Practitioner, Organization)</description></item>
/// </list>
/// <para><strong>Example Usage - Basic CareTeam:</strong></para>
/// <code>
/// var careTeam = CareTeamBuilder.Create(schemaProvider)
///     .WithName("Cardiac Care Team")
///     .WithSubject(patientId)
///     .WithPractitionerParticipant(practitionerId, "223366009")
///     .WithTag(tag)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Multiple Participants with Roles:</strong></para>
/// <code>
/// var careTeam = CareTeamBuilder.Create(schemaProvider)
///     .WithName("Primary Care Team")
///     .WithStatus("active")
///     .WithSubject(patientId)
///     .WithPractitionerParticipant(doctor1Id, "17561000", "http://snomed.info/sct")
///     .WithPractitionerParticipant(doctor2Id, "223366009", "http://snomed.info/sct")
///     .WithOrganizationParticipant(orgId)
///     .WithPatientParticipant(patientId)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Include Testing:</strong></para>
/// <code>
/// // Create care team with mixed participant types for _include testing
/// var careTeam = CareTeamBuilder.Create(schemaProvider)
///     .WithName("Multi-Disciplinary Team")
///     .WithSubject(primaryPatientId)
///     .WithPatientParticipant(patient1Id)
///     .WithPatientParticipant(patient2Id)
///     .WithPractitionerParticipant(practitionerId, "223366009")
///     .WithOrganizationParticipant(organizationId)
///     .WithTag(tag)
///     .Build();
///
/// // Search: GET /CareTeam?_tag={tag}&amp;_include=CareTeam:participant
/// </code>
/// </remarks>
public sealed class CareTeamBuilder : FhirResourceBuilder<CareTeamBuilder>
{
    private string _status = "active";
    private string? _name;
    private string? _subjectId;

    private readonly List<ParticipantInfo> _participants = [];

    private CareTeamBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new CareTeamBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
    /// <returns>A new CareTeamBuilder instance.</returns>
    /// <example>
    /// <code>
    /// var builder = CareTeamBuilder.Create(schemaProvider);
    /// </code>
    /// </example>
    public static CareTeamBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new CareTeamBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the care team status.
    /// </summary>
    /// <param name="status">The care team status (e.g., "active", "suspended", "inactive", "proposed", "entered-in-error").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithStatus("suspended")
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the care team name.
    /// </summary>
    /// <param name="name">The care team name.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithName("Cardiac Care Team")
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the subject reference to a Patient.
    /// </summary>
    /// <param name="patientId">The patient ID to reference.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithSubject(patientId)
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectId = patientId;
        return this;
    }

    /// <summary>
    /// Adds a participant to the care team with an optional role.
    /// </summary>
    /// <param name="resourceType">The participant resource type (e.g., "Patient", "Practitioner", "Organization").</param>
    /// <param name="id">The participant resource ID.</param>
    /// <param name="roleCode">Optional role code (e.g., "223366009" for Healthcare professional).</param>
    /// <param name="roleSystem">Optional role code system (defaults to http://snomed.info/sct if roleCode is provided).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithParticipant("Practitioner", practitionerId, "17561000", "http://snomed.info/sct")
    ///     .WithParticipant("Organization", orgId)
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithParticipant(string resourceType, string id, string? roleCode = null, string? roleSystem = null)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);

        _participants.Add(new ParticipantInfo(resourceType, id, roleCode, roleSystem));
        return this;
    }

    /// <summary>
    /// Adds a Patient participant to the care team with an optional role.
    /// </summary>
    /// <param name="patientId">The patient ID to add as a participant.</param>
    /// <param name="roleCode">Optional role code for the patient's role in the care team.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithPatientParticipant(patientId)
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithPatientParticipant(string patientId, string? roleCode = null)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        return WithParticipant("Patient", patientId, roleCode, roleCode is not null ? "http://snomed.info/sct" : null);
    }

    /// <summary>
    /// Adds a Practitioner participant to the care team with an optional role.
    /// </summary>
    /// <param name="practitionerId">The practitioner ID to add as a participant.</param>
    /// <param name="roleCode">Optional role code for the practitioner (defaults to "223366009" - Healthcare professional).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithPractitionerParticipant(practitionerId, "17561000") // Cardiologist
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithPractitionerParticipant(string practitionerId, string? roleCode = null)
    {
        ArgumentNullException.ThrowIfNull(practitionerId);
        return WithParticipant("Practitioner", practitionerId, roleCode ?? "223366009", "http://snomed.info/sct");
    }

    /// <summary>
    /// Adds an Organization participant to the care team with an optional role.
    /// </summary>
    /// <param name="organizationId">The organization ID to add as a participant.</param>
    /// <param name="roleCode">Optional role code for the organization.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithOrganizationParticipant(organizationId)
    ///     .Build();
    /// </code>
    /// </example>
    public CareTeamBuilder WithOrganizationParticipant(string organizationId, string? roleCode = null)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        return WithParticipant("Organization", organizationId, roleCode, roleCode is not null ? "http://snomed.info/sct" : null);
    }

    /// <summary>
    /// Builds the CareTeam resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the built CareTeam resource.</returns>
    /// <example>
    /// <code>
    /// var careTeam = CareTeamBuilder.Create(schemaProvider)
    ///     .WithName("Primary Care Team")
    ///     .WithStatus("active")
    ///     .WithSubject(patientId)
    ///     .WithPractitionerParticipant(practitionerId)
    ///     .WithTag(tag)
    ///     .Build();
    /// </code>
    /// </example>
    public override ResourceJsonNode Build()
    {
        var careTeamJson = new JsonObject
        {
            ["resourceType"] = "CareTeam",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status
        };

        if (!string.IsNullOrEmpty(_name))
        {
            careTeamJson["name"] = _name;
        }

        if (!string.IsNullOrEmpty(_subjectId))
        {
            careTeamJson["subject"] = CreateReference("Patient", _subjectId);
        }

        if (_participants.Count > 0)
        {
            careTeamJson["participant"] = BuildParticipants();
        }

        var json = careTeamJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    private JsonArray BuildParticipants()
    {
        var participantArray = new JsonArray();

        foreach (var participant in _participants)
        {
            var participantJson = new JsonObject
            {
                ["member"] = CreateReference(participant.ResourceType, participant.Id)
            };

            if (!string.IsNullOrEmpty(participant.RoleCode))
            {
                var roleSystem = participant.RoleSystem ?? "http://snomed.info/sct";
                participantJson["role"] = new JsonArray
                {
                    CreateCodeableConcept(participant.RoleCode, roleSystem)
                };
            }

            participantArray.Add(participantJson);
        }

        return participantArray;
    }

    private readonly record struct ParticipantInfo(string ResourceType, string Id, string? RoleCode, string? RoleSystem);
}
