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
/// Fluent builder for generating HealthcareService resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// <para><strong>Basic Usage:</strong></para>
/// <code>
/// var service = HealthcareServiceBuilder.Create(schemaProvider)
///     .WithName("General Surgery")
///     .WithProvidedBy(organizationId)
///     .Build();
/// </code>
///
/// <para><strong>Service with Category and Types:</strong></para>
/// <code>
/// var service = HealthcareServiceBuilder.Create(schemaProvider)
///     .WithName("Cardiology Services")
///     .WithProvidedBy(hospitalOrgId)
///     .WithCategory("35", "http://terminology.hl7.org/CodeSystem/service-category", "Specialist Medical")
///     .WithServiceType("165", "http://snomed.info/sct", "Cardiology")
///     .WithServiceType("166", "http://snomed.info/sct", "Cardiac Surgery")
///     .Build();
/// </code>
///
/// <para><strong>Service with Locations and Contact Details:</strong></para>
/// <code>
/// var service = HealthcareServiceBuilder.Create(schemaProvider)
///     .WithName("Emergency Department")
///     .WithProvidedBy(organizationId)
///     .WithLocation(mainLocationId)
///     .WithLocation(secondaryLocationId)
///     .WithPhone("555-0100")
///     .WithEmail("emergency@hospital.org")
///     .WithActive(true)
///     .WithTag(testTag)
///     .Build();
/// </code>
///
/// <para><strong>Complete Service with All Properties:</strong></para>
/// <code>
/// var service = HealthcareServiceBuilder.Create(schemaProvider)
///     .WithId("svc-001")
///     .WithName("Primary Care Services")
///     .WithProvidedBy(organizationId)
///     .WithCategory("17", "http://terminology.hl7.org/CodeSystem/service-category", "General Practice")
///     .WithServiceType("1", "http://terminology.hl7.org/CodeSystem/service-type", "Adoption/Permanent Care Info/Support")
///     .WithLocation(locationId)
///     .WithPhone("555-0199")
///     .WithEmail("primarycare@clinic.org")
///     .WithActive(true)
///     .WithTag(testTag)
///     .Build();
/// </code>
/// </remarks>
public sealed class HealthcareServiceBuilder : FhirResourceBuilder<HealthcareServiceBuilder>
{
    private bool _active = true;
    private string? _name;
    private string? _providedByOrganizationId;
    private (string Code, string System, string? Display)? _category;
    private readonly List<(string Code, string System, string? Display)> _serviceTypes = [];
    private readonly List<string> _locationIds = [];
    private string? _phone;
    private string? _email;

    private HealthcareServiceBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new HealthcareServiceBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for generating base resources.</param>
    /// <returns>A new HealthcareServiceBuilder instance</returns>
    public static HealthcareServiceBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new HealthcareServiceBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets whether the service is active.
    /// </summary>
    /// <param name="active">True if the service is active (default: true)</param>
    /// <returns>This builder for method chaining</returns>
    public HealthcareServiceBuilder WithActive(bool active)
    {
        _active = active;
        return this;
    }

    /// <summary>
    /// Sets the service name.
    /// </summary>
    /// <param name="name">The service name (e.g., "Cardiology Services", "Emergency Department")</param>
    /// <returns>This builder for method chaining</returns>
    public HealthcareServiceBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the organization that provides this service.
    /// </summary>
    /// <param name="organizationId">The organization resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var service = HealthcareServiceBuilder.Create(schemaProvider)
    ///     .WithProvidedBy(organization.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public HealthcareServiceBuilder WithProvidedBy(string organizationId)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        _providedByOrganizationId = organizationId;
        return this;
    }

    /// <summary>
    /// Sets the service category.
    /// </summary>
    /// <param name="code">The category code</param>
    /// <param name="system">The code system (default: http://terminology.hl7.org/CodeSystem/service-category)</param>
    /// <param name="display">Optional display text for the code</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var service = HealthcareServiceBuilder.Create(schemaProvider)
    ///     .WithCategory("35", "http://terminology.hl7.org/CodeSystem/service-category", "Specialist Medical")
    ///     .Build();
    /// </code>
    /// </example>
    public HealthcareServiceBuilder WithCategory(
        string code,
        string? system = null,
        string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _category = (code, system ?? "http://terminology.hl7.org/CodeSystem/service-category", display);
        return this;
    }

    /// <summary>
    /// Adds a service type to the service.
    /// Can be called multiple times to add multiple service types.
    /// </summary>
    /// <param name="code">The type code</param>
    /// <param name="system">The code system (default: http://terminology.hl7.org/CodeSystem/service-type)</param>
    /// <param name="display">Optional display text for the code</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var service = HealthcareServiceBuilder.Create(schemaProvider)
    ///     .WithServiceType("165", "http://snomed.info/sct", "Cardiology")
    ///     .WithServiceType("166", "http://snomed.info/sct", "Cardiac Surgery")
    ///     .Build();
    /// </code>
    /// </example>
    public HealthcareServiceBuilder WithServiceType(
        string code,
        string? system = null,
        string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _serviceTypes.Add((code, system ?? "http://terminology.hl7.org/CodeSystem/service-type", display));
        return this;
    }

    /// <summary>
    /// Adds a location where this service is provided.
    /// Can be called multiple times to add multiple locations.
    /// </summary>
    /// <param name="locationId">The location resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var service = HealthcareServiceBuilder.Create(schemaProvider)
    ///     .WithLocation(mainLocationId)
    ///     .WithLocation(secondaryLocationId)
    ///     .Build();
    /// </code>
    /// </example>
    public HealthcareServiceBuilder WithLocation(string locationId)
    {
        ArgumentNullException.ThrowIfNull(locationId);
        _locationIds.Add(locationId);
        return this;
    }

    /// <summary>
    /// Sets the service's phone number.
    /// </summary>
    /// <param name="phone">The phone number</param>
    /// <returns>This builder for method chaining</returns>
    public HealthcareServiceBuilder WithPhone(string phone)
    {
        ArgumentNullException.ThrowIfNull(phone);
        _phone = phone;
        return this;
    }

    /// <summary>
    /// Sets the service's email address.
    /// </summary>
    /// <param name="email">The email address</param>
    /// <returns>This builder for method chaining</returns>
    public HealthcareServiceBuilder WithEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        _email = email;
        return this;
    }

    /// <summary>
    /// Builds the HealthcareService resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the HealthcareService resource</returns>
    public override ResourceJsonNode Build()
    {
        var serviceJson = new JsonObject
        {
            ["resourceType"] = "HealthcareService",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["active"] = _active
        };

        if (!string.IsNullOrEmpty(_name))
        {
            serviceJson["name"] = _name;
        }

        if (!string.IsNullOrEmpty(_providedByOrganizationId))
        {
            serviceJson["providedBy"] = CreateReference("Organization", _providedByOrganizationId);
        }

        if (_category.HasValue)
        {
            serviceJson["category"] = new JsonArray
            {
                CreateCodeableConcept(
                    _category.Value.Code,
                    _category.Value.System,
                    _category.Value.Display)
            };
        }

        if (_serviceTypes.Count > 0)
        {
            serviceJson["type"] = BuildServiceTypes();
        }

        if (_locationIds.Count > 0)
        {
            serviceJson["location"] = BuildLocationReferences();
        }

        var telecomArray = BuildTelecom();
        if (telecomArray.Count > 0)
        {
            serviceJson["telecom"] = telecomArray;
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(serviceJson);
    }

    private JsonArray BuildServiceTypes()
    {
        var typesArray = new JsonArray();

        foreach (var (code, system, display) in _serviceTypes)
        {
            typesArray.Add(CreateCodeableConcept(code, system, display));
        }

        return typesArray;
    }

    private JsonArray BuildLocationReferences()
    {
        var locationsArray = new JsonArray();

        foreach (var locationId in _locationIds)
        {
            locationsArray.Add(CreateReference("Location", locationId));
        }

        return locationsArray;
    }

    private JsonArray BuildTelecom()
    {
        var telecom = new JsonArray();

        if (!string.IsNullOrEmpty(_phone))
        {
            telecom.Add(new JsonObject
            {
                ["system"] = "phone",
                ["value"] = _phone,
                ["use"] = "work"
            });
        }

        if (!string.IsNullOrEmpty(_email))
        {
            telecom.Add(new JsonObject
            {
                ["system"] = "email",
                ["value"] = _email,
                ["use"] = "work"
            });
        }

        return telecom;
    }
}
