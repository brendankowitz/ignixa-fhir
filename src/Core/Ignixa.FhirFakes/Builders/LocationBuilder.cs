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
/// Fluent builder for generating Location resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// <para><strong>Basic Usage:</strong></para>
/// <code>
/// // Simple location with name and status
/// var location = LocationBuilder.Create(schemaProvider)
///     .WithName("Main Clinic")
///     .WithStatus("active")
///     .Build();
/// </code>
///
/// <para><strong>Location with Address:</strong></para>
/// <code>
/// var location = LocationBuilder.Create(schemaProvider)
///     .WithName("Boston Medical Center")
///     .WithAddress("725 Albany St", "Boston", "MA", "02118")
///     .WithTag(testTag)
///     .Build();
/// </code>
///
/// <para><strong>Location Hierarchy (Building → Floor → Room):</strong></para>
/// <code>
/// // Create building
/// var building = LocationBuilder.Create(schemaProvider)
///     .WithName("Main Building")
///     .WithManagingOrganization(hospitalOrgId)
///     .Build();
///
/// // Create floor within building
/// var floor = LocationBuilder.Create(schemaProvider)
///     .WithName("First Floor")
///     .WithPartOf(building.Id!)
///     .Build();
///
/// // Create room within floor
/// var room = LocationBuilder.Create(schemaProvider)
///     .WithName("Room 101")
///     .WithPartOf(floor.Id!)
///     .Build();
/// </code>
///
/// <para><strong>Complete Location with All Properties:</strong></para>
/// <code>
/// var location = LocationBuilder.Create(schemaProvider)
///     .WithId("loc-001")
///     .WithName("Emergency Department")
///     .WithStatus("active")
///     .WithManagingOrganization(organizationId)
///     .WithAddress("100 Medical Plaza", "Seattle", "WA", "98101")
///     .WithTag(testTag)
///     .Build();
/// </code>
/// </remarks>
public sealed class LocationBuilder : FhirResourceBuilder<LocationBuilder>
{
    private string? _name;
    private string _status = "active";
    private string? _managingOrganizationId;
    private string? _partOfLocationId;

    // Address fields
    private string? _addressLine;
    private string? _addressCity;
    private string? _addressState;
    private string? _addressZip;

    private LocationBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new LocationBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for generating base resources.</param>
    /// <returns>A new LocationBuilder instance</returns>
    public static LocationBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new LocationBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the location's name.
    /// </summary>
    /// <param name="name">The location name (e.g., "Main Clinic", "Emergency Room")</param>
    /// <returns>This builder for method chaining</returns>
    public LocationBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the location's status.
    /// </summary>
    /// <param name="status">The status code (e.g., "active", "suspended", "inactive"). Default is "active".</param>
    /// <returns>This builder for method chaining</returns>
    public LocationBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the managing organization reference.
    /// </summary>
    /// <param name="orgId">The organization resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var location = LocationBuilder.Create(schemaProvider)
    ///     .WithManagingOrganization(organization.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public LocationBuilder WithManagingOrganization(string orgId)
    {
        ArgumentNullException.ThrowIfNull(orgId);
        _managingOrganizationId = orgId;
        return this;
    }

    /// <summary>
    /// Sets the parent location reference (partOf).
    /// </summary>
    /// <param name="locationId">The parent location resource ID (not the full reference path)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// // Create a building and a room within it
    /// var building = LocationBuilder.Create(schemaProvider)
    ///     .WithName("Main Building")
    ///     .Build();
    ///
    /// var room = LocationBuilder.Create(schemaProvider)
    ///     .WithName("Room 101")
    ///     .WithPartOf(building.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public LocationBuilder WithPartOf(string locationId)
    {
        ArgumentNullException.ThrowIfNull(locationId);
        _partOfLocationId = locationId;
        return this;
    }

    /// <summary>
    /// Sets the location's address with all components.
    /// </summary>
    /// <param name="line">Street address line</param>
    /// <param name="city">City name</param>
    /// <param name="state">State or province</param>
    /// <param name="zip">Postal code</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var location = LocationBuilder.Create(schemaProvider)
    ///     .WithAddress("725 Albany St", "Boston", "MA", "02118")
    ///     .Build();
    /// </code>
    /// </example>
    public LocationBuilder WithAddress(string line, string city, string state, string zip)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(zip);

        _addressLine = line;
        _addressCity = city;
        _addressState = state;
        _addressZip = zip;
        return this;
    }

    /// <summary>
    /// Builds the Location resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Location resource</returns>
    public override ResourceJsonNode Build()
    {
        var locationJson = new JsonObject
        {
            ["resourceType"] = "Location",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status
        };

        // Optional name
        if (!string.IsNullOrEmpty(_name))
        {
            locationJson["name"] = _name;
        }

        // Optional managingOrganization reference
        if (!string.IsNullOrEmpty(_managingOrganizationId))
        {
            locationJson["managingOrganization"] = CreateReference("Organization", _managingOrganizationId);
        }

        // Optional partOf reference
        if (!string.IsNullOrEmpty(_partOfLocationId))
        {
            locationJson["partOf"] = CreateReference("Location", _partOfLocationId);
        }

        // Optional address
        if (HasAddress())
        {
            locationJson["address"] = BuildAddress();
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(locationJson);
    }

    private bool HasAddress()
    {
        return !string.IsNullOrEmpty(_addressLine) ||
               !string.IsNullOrEmpty(_addressCity) ||
               !string.IsNullOrEmpty(_addressState) ||
               !string.IsNullOrEmpty(_addressZip);
    }

    private JsonObject BuildAddress()
    {
        var addressJson = new JsonObject();

        if (!string.IsNullOrEmpty(_addressLine))
        {
            addressJson["line"] = new JsonArray { _addressLine };
        }

        if (!string.IsNullOrEmpty(_addressCity))
        {
            addressJson["city"] = _addressCity;
        }

        if (!string.IsNullOrEmpty(_addressState))
        {
            addressJson["state"] = _addressState;
        }

        if (!string.IsNullOrEmpty(_addressZip))
        {
            addressJson["postalCode"] = _addressZip;
        }

        return addressJson;
    }
}
