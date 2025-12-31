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
/// Fluent builder for generating PractitionerRole resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// Example Usage:
/// <code>
/// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
///     .WithPractitioner("practitioner-123")
///     .WithOrganization("org-456")
///     .AddRole("doctor", "http://terminology.hl7.org/CodeSystem/practitioner-role", "Doctor")
///     .AddSpecialty("394579002", "http://snomed.info/sct", "Cardiology")
///     .AddLocation("location-789")
///     .WithActive(true)
///     .WithTag(tag)
///     .Build();
///
/// // Or for a simple practitioner-organization link:
/// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
///     .WithPractitioner("practitioner-123")
///     .WithOrganization("org-456")
///     .Build();
/// </code>
/// </remarks>
public sealed class PractitionerRoleBuilder : FhirResourceBuilder<PractitionerRoleBuilder>
{
    private bool _active = true;
    private string? _practitionerId;
    private string? _organizationId;
    private readonly List<(string Code, string? System, string? Display)> _roles = [];
    private readonly List<(string Code, string? System, string? Display)> _specialties = [];
    private readonly List<string> _locationIds = [];
    private readonly List<string> _healthcareServiceIds = [];

    private PractitionerRoleBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new PractitionerRoleBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for generating base resources.</param>
    /// <returns>A new PractitionerRoleBuilder instance</returns>
    public static PractitionerRoleBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new PractitionerRoleBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets whether the practitioner role is currently active.
    /// </summary>
    /// <param name="active">True if the role is active, false otherwise</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .WithActive(false)
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder WithActive(bool active)
    {
        _active = active;
        return this;
    }

    /// <summary>
    /// Sets the reference to the Practitioner resource.
    /// </summary>
    /// <param name="practitionerId">The ID of the Practitioner resource</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .WithPractitioner("practitioner-123")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder WithPractitioner(string practitionerId)
    {
        ArgumentNullException.ThrowIfNull(practitionerId);
        _practitionerId = practitionerId;
        return this;
    }

    /// <summary>
    /// Sets the reference to the Organization resource where the practitioner provides services.
    /// </summary>
    /// <param name="organizationId">The ID of the Organization resource</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .WithOrganization("org-456")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder WithOrganization(string organizationId)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        _organizationId = organizationId;
        return this;
    }

    /// <summary>
    /// Adds a role that the practitioner performs at the organization.
    /// </summary>
    /// <param name="code">The role code (e.g., "doctor", "nurse")</param>
    /// <param name="system">Optional code system (defaults to http://terminology.hl7.org/CodeSystem/practitioner-role)</param>
    /// <param name="display">Optional display text (e.g., "Doctor", "Nurse")</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .AddRole("doctor", display: "Doctor")
    ///     .AddRole("researcher", "http://terminology.hl7.org/CodeSystem/practitioner-role", "Researcher")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder AddRole(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _roles.Add((code, system ?? "http://terminology.hl7.org/CodeSystem/practitioner-role", display));
        return this;
    }

    /// <summary>
    /// Adds a specialty that the practitioner has expertise in.
    /// </summary>
    /// <param name="code">The specialty code (e.g., "394579002" for Cardiology)</param>
    /// <param name="system">Optional code system (defaults to http://snomed.info/sct)</param>
    /// <param name="display">Optional display text (e.g., "Cardiology")</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .AddSpecialty("394579002", "http://snomed.info/sct", "Cardiology")
    ///     .AddSpecialty("419192003", display: "Internal Medicine")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder AddSpecialty(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _specialties.Add((code, system ?? "http://snomed.info/sct", display));
        return this;
    }

    /// <summary>
    /// Adds a reference to a Location where the practitioner provides services.
    /// </summary>
    /// <param name="locationId">The ID of the Location resource</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .AddLocation("location-789")
    ///     .AddLocation("location-101")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder AddLocation(string locationId)
    {
        ArgumentNullException.ThrowIfNull(locationId);
        _locationIds.Add(locationId);
        return this;
    }

    /// <summary>
    /// Adds a reference to a HealthcareService that the practitioner provides.
    /// </summary>
    /// <param name="healthcareServiceId">The ID of the HealthcareService resource</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitionerRole = PractitionerRoleBuilder.Create(schemaProvider)
    ///     .AddHealthcareService("service-123")
    ///     .AddHealthcareService("service-456")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerRoleBuilder AddHealthcareService(string healthcareServiceId)
    {
        ArgumentNullException.ThrowIfNull(healthcareServiceId);
        _healthcareServiceIds.Add(healthcareServiceId);
        return this;
    }

    /// <summary>
    /// Builds the PractitionerRole resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the PractitionerRole resource</returns>
    public override ResourceJsonNode Build()
    {
        var practitionerRoleJson = new JsonObject
        {
            ["resourceType"] = "PractitionerRole",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["active"] = _active
        };

        if (!string.IsNullOrEmpty(_practitionerId))
        {
            practitionerRoleJson["practitioner"] = CreateReference("Practitioner", _practitionerId);
        }

        if (!string.IsNullOrEmpty(_organizationId))
        {
            practitionerRoleJson["organization"] = CreateReference("Organization", _organizationId);
        }

        if (_roles.Count > 0)
        {
            practitionerRoleJson["code"] = BuildCodeableConcepts(_roles);
        }

        if (_specialties.Count > 0)
        {
            practitionerRoleJson["specialty"] = BuildCodeableConcepts(_specialties);
        }

        if (_locationIds.Count > 0)
        {
            practitionerRoleJson["location"] = BuildLocationReferences();
        }

        if (_healthcareServiceIds.Count > 0)
        {
            practitionerRoleJson["healthcareService"] = BuildHealthcareServiceReferences();
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(practitionerRoleJson);
    }

    private JsonArray BuildCodeableConcepts(List<(string Code, string? System, string? Display)> items)
    {
        var array = new JsonArray();

        foreach (var (code, system, display) in items)
        {
            var coding = new JsonObject
            {
                ["code"] = code
            };

            if (system is not null)
            {
                coding["system"] = system;
            }

            if (display is not null)
            {
                coding["display"] = display;
            }

            array.Add(new JsonObject
            {
                ["coding"] = new JsonArray { coding }
            });
        }

        return array;
    }

    private JsonArray BuildLocationReferences()
    {
        var array = new JsonArray();

        foreach (var locationId in _locationIds)
        {
            array.Add(CreateReference("Location", locationId));
        }

        return array;
    }

    private JsonArray BuildHealthcareServiceReferences()
    {
        var array = new JsonArray();

        foreach (var serviceId in _healthcareServiceIds)
        {
            array.Add(CreateReference("HealthcareService", serviceId));
        }

        return array;
    }
}
