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
/// Fluent builder for generating OrganizationAffiliation resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// <para><strong>Basic Usage:</strong></para>
/// <code>
/// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
///     .WithOrganization(hospitalId)
///     .WithParticipatingOrganization(clinicId)
///     .Build();
/// </code>
///
/// <para><strong>Network Affiliation:</strong></para>
/// <code>
/// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
///     .WithOrganization(organizationId)
///     .WithParticipatingOrganization(memberId)
///     .AddNetwork(networkId)
///     .AddCode("member", "http://hl7.org/fhir/organization-role", "Member")
///     .Build();
/// </code>
///
/// <para><strong>Complete Affiliation with Locations and Services:</strong></para>
/// <code>
/// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
///     .WithId("aff-001")
///     .WithActive(true)
///     .WithOrganization(hospitalId)
///     .WithParticipatingOrganization(departmentId)
///     .AddCode("dept", "http://terminology.hl7.org/CodeSystem/organization-role", "Department")
///     .AddSpecialty("394802001", "http://snomed.info/sct", "General Medicine")
///     .AddLocation(locationId)
///     .AddHealthcareService(serviceId)
///     .WithTag(testTag)
///     .Build();
/// </code>
/// </remarks>
public sealed class OrganizationAffiliationBuilder : FhirResourceBuilder<OrganizationAffiliationBuilder>
{
    private bool _active = true;
    private string? _organizationId;
    private string? _participatingOrganizationId;

    private readonly List<string> _networkIds = [];
    private readonly List<(string Code, string? System, string? Display)> _codes = [];
    private readonly List<(string Code, string? System, string? Display)> _specialties = [];
    private readonly List<string> _locationIds = [];
    private readonly List<string> _healthcareServiceIds = [];

    private OrganizationAffiliationBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new OrganizationAffiliationBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for generating base resources.</param>
    /// <returns>A new OrganizationAffiliationBuilder instance</returns>
    public static OrganizationAffiliationBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new OrganizationAffiliationBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets whether the affiliation is active.
    /// </summary>
    /// <param name="active">True if active, false if inactive. Default is true.</param>
    /// <returns>This builder for method chaining</returns>
    public OrganizationAffiliationBuilder WithActive(bool active)
    {
        _active = active;
        return this;
    }

    /// <summary>
    /// Sets the organization providing services.
    /// </summary>
    /// <param name="organizationId">The organization resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .WithOrganization(hospital.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder WithOrganization(string organizationId)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        _organizationId = organizationId;
        return this;
    }

    /// <summary>
    /// Sets the organization that participates in the affiliation.
    /// </summary>
    /// <param name="participatingOrganizationId">The participating organization resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .WithParticipatingOrganization(clinic.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder WithParticipatingOrganization(string participatingOrganizationId)
    {
        ArgumentNullException.ThrowIfNull(participatingOrganizationId);
        _participatingOrganizationId = participatingOrganizationId;
        return this;
    }

    /// <summary>
    /// Adds a network organization reference.
    /// Can be called multiple times to add multiple networks.
    /// </summary>
    /// <param name="networkId">The network organization resource ID</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .AddNetwork(network1.Id!)
    ///     .AddNetwork(network2.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder AddNetwork(string networkId)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        _networkIds.Add(networkId);
        return this;
    }

    /// <summary>
    /// Adds a role/relationship type code.
    /// Can be called multiple times to add multiple codes.
    /// </summary>
    /// <param name="code">The code value (e.g., "member", "dept", "provider")</param>
    /// <param name="system">Optional code system URI. Defaults to http://hl7.org/fhir/organization-role if not specified.</param>
    /// <param name="display">Optional display text</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .AddCode("member", display: "Member")
    ///     .AddCode("provider", "http://hl7.org/fhir/organization-role", "Provider")
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder AddCode(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _codes.Add((code, system, display));
        return this;
    }

    /// <summary>
    /// Adds a specialty code.
    /// Can be called multiple times to add multiple specialties.
    /// </summary>
    /// <param name="code">The specialty code value</param>
    /// <param name="system">Optional code system URI (e.g., "http://snomed.info/sct")</param>
    /// <param name="display">Optional display text</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .AddSpecialty("394802001", "http://snomed.info/sct", "General Medicine")
    ///     .AddSpecialty("394814009", "http://snomed.info/sct", "General Practice")
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder AddSpecialty(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _specialties.Add((code, system, display));
        return this;
    }

    /// <summary>
    /// Adds a location reference.
    /// Can be called multiple times to add multiple locations.
    /// </summary>
    /// <param name="locationId">The location resource ID</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .AddLocation(location1.Id!)
    ///     .AddLocation(location2.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder AddLocation(string locationId)
    {
        ArgumentNullException.ThrowIfNull(locationId);
        _locationIds.Add(locationId);
        return this;
    }

    /// <summary>
    /// Adds a healthcare service reference.
    /// Can be called multiple times to add multiple services.
    /// </summary>
    /// <param name="healthcareServiceId">The healthcare service resource ID</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var affiliation = OrganizationAffiliationBuilder.Create(schemaProvider)
    ///     .AddHealthcareService(service1.Id!)
    ///     .AddHealthcareService(service2.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public OrganizationAffiliationBuilder AddHealthcareService(string healthcareServiceId)
    {
        ArgumentNullException.ThrowIfNull(healthcareServiceId);
        _healthcareServiceIds.Add(healthcareServiceId);
        return this;
    }

    /// <summary>
    /// Builds the OrganizationAffiliation resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the OrganizationAffiliation resource</returns>
    public override ResourceJsonNode Build()
    {
        var affiliationJson = new JsonObject
        {
            ["resourceType"] = "OrganizationAffiliation",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["active"] = _active
        };

        if (!string.IsNullOrEmpty(_organizationId))
        {
            affiliationJson["organization"] = CreateReference("Organization", _organizationId);
        }

        if (!string.IsNullOrEmpty(_participatingOrganizationId))
        {
            affiliationJson["participatingOrganization"] = CreateReference("Organization", _participatingOrganizationId);
        }

        if (_networkIds.Count > 0)
        {
            affiliationJson["network"] = BuildNetworks();
        }

        if (_codes.Count > 0)
        {
            affiliationJson["code"] = BuildCodeableConcepts(_codes);
        }

        if (_specialties.Count > 0)
        {
            affiliationJson["specialty"] = BuildCodeableConcepts(_specialties);
        }

        if (_locationIds.Count > 0)
        {
            affiliationJson["location"] = BuildReferences("Location", _locationIds);
        }

        if (_healthcareServiceIds.Count > 0)
        {
            affiliationJson["healthcareService"] = BuildReferences("HealthcareService", _healthcareServiceIds);
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(affiliationJson);
    }

    private JsonArray BuildNetworks()
    {
        var networks = new JsonArray();

        foreach (var networkId in _networkIds)
        {
            networks.Add(CreateReference("Organization", networkId));
        }

        return networks;
    }

    private static JsonArray BuildCodeableConcepts(List<(string Code, string? System, string? Display)> items)
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

            var concept = new JsonObject
            {
                ["coding"] = new JsonArray { coding }
            };

            array.Add(concept);
        }

        return array;
    }

    private static JsonArray BuildReferences(string resourceType, List<string> ids)
    {
        var references = new JsonArray();

        foreach (var id in ids)
        {
            references.Add(CreateReference(resourceType, id));
        }

        return references;
    }
}
