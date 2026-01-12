// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating Organization resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// Example Usage:
/// <code>
/// var org = OrganizationBuilder.Create(schemaProvider)
///     .WithName("Boston Medical Center")
///     .WithIdentifier("12345")
///     .WithAddress("725 Albany St", "Boston", "MA", "02118")
///     .WithType("practice")
///     .WithTag(tag)
///     .Build();
/// </code>
/// </remarks>
public sealed class OrganizationBuilder : FhirResourceBuilder<OrganizationBuilder>
{
    private string? _name;
    private string? _npiNumber;
    private string? _taxId;
    private bool _active = true;

    // Identifiers (beyond NPI/Tax ID)
    private readonly List<(string? System, string Value)> _identifiers = [];

    // Address fields
    private readonly List<OrganizationAddress> _addresses = [];

    // Type codes
    private readonly List<(string? System, string Code, string? Display)> _typeCodes = [];

    // Contact info
    private string? _phone;
    private string? _email;

    // Reference fields
    private string? _partOfOrganizationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizationBuilder"/> class.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    public OrganizationBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new OrganizationBuilder instance.
    /// </summary>
    public static OrganizationBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new OrganizationBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the organization's name.
    /// </summary>
    public OrganizationBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the organization's NPI number.
    /// If not provided, a valid NPI will be auto-generated.
    /// </summary>
    public OrganizationBuilder WithNpi(string npi)
    {
        ArgumentNullException.ThrowIfNull(npi);
        _npiNumber = npi;
        return this;
    }

    /// <summary>
    /// Sets the organization's Tax ID (EIN/TIN).
    /// If not provided, a synthetic Tax ID will be auto-generated.
    /// </summary>
    public OrganizationBuilder WithTaxId(string taxId)
    {
        ArgumentNullException.ThrowIfNull(taxId);
        _taxId = taxId;
        return this;
    }

    /// <summary>
    /// Adds a custom identifier to the organization.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <param name="system">Optional identifier system URI.</param>
    public OrganizationBuilder WithIdentifier(string value, string? system = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        _identifiers.Add((system, value));
        return this;
    }

    /// <summary>
    /// Adds an address to the organization.
    /// </summary>
    public OrganizationBuilder WithAddress(string line, string city, string state, string postalCode, string country = "USA")
    {
        _addresses.Add(new OrganizationAddress(line, city, state, postalCode, country));
        return this;
    }

    /// <summary>
    /// Adds an address with just a city (useful for tests that only care about city).
    /// </summary>
    public OrganizationBuilder WithCity(string city)
    {
        ArgumentNullException.ThrowIfNull(city);
        _addresses.Add(new OrganizationAddress(string.Empty, city, string.Empty, string.Empty, "USA"));
        return this;
    }

    /// <summary>
    /// Adds a type code to the organization.
    /// </summary>
    /// <param name="code">The type code (e.g., "practice", "prov", "dept").</param>
    /// <param name="system">Optional code system (defaults to http://terminology.hl7.org/CodeSystem/organization-type).</param>
    /// <param name="display">Optional display text.</param>
    public OrganizationBuilder WithType(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _typeCodes.Add((system, code, display));
        return this;
    }

    /// <summary>
    /// Sets the organization's phone number.
    /// </summary>
    public OrganizationBuilder WithPhone(string phone)
    {
        ArgumentNullException.ThrowIfNull(phone);
        _phone = phone;
        return this;
    }

    /// <summary>
    /// Sets the organization's email address.
    /// </summary>
    public OrganizationBuilder WithEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        _email = email;
        return this;
    }

    /// <summary>
    /// Sets the parent organization reference (partOf).
    /// </summary>
    /// <param name="organizationId">The ID of the parent organization.</param>
    public OrganizationBuilder WithPartOf(string organizationId)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        _partOfOrganizationId = organizationId;
        return this;
    }

    /// <summary>
    /// Sets whether the organization is active.
    /// </summary>
    public OrganizationBuilder WithActive(bool active)
    {
        _active = active;
        return this;
    }

    /// <summary>
    /// Builds the Organization resource with all configured properties.
    /// </summary>
    public override ResourceJsonNode Build()
    {
        var orgJson = new JsonObject
        {
            ["resourceType"] = "Organization",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["active"] = _active
        };

        if (!string.IsNullOrEmpty(_name))
        {
            orgJson["name"] = _name;
        }

        // Build identifiers
        var identifierArray = BuildIdentifiers();
        if (identifierArray.Count > 0)
        {
            orgJson["identifier"] = identifierArray;
        }

        // Build addresses
        if (_addresses.Count > 0)
        {
            orgJson["address"] = BuildAddresses();
        }

        // Build type
        if (_typeCodes.Count > 0)
        {
            orgJson["type"] = BuildTypes();
        }

        // Build telecom
        var telecomArray = BuildTelecom();
        if (telecomArray.Count > 0)
        {
            orgJson["telecom"] = telecomArray;
        }

        // Build partOf reference
        if (!string.IsNullOrEmpty(_partOfOrganizationId))
        {
            orgJson["partOf"] = new JsonObject
            {
                ["reference"] = $"Organization/{_partOfOrganizationId}"
            };
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(orgJson);
    }

    private JsonArray BuildIdentifiers()
    {
        var identifiers = new JsonArray();

        // Add NPI if present or generate
        var npi = _npiNumber ?? OrganizationState.GenerateNpi();
        identifiers.Add(new JsonObject
        {
            ["system"] = OrganizationState.NpiSystem,
            ["value"] = npi
        });

        // Add Tax ID if present or generate
        var taxId = _taxId ?? OrganizationState.GenerateTaxId();
        identifiers.Add(new JsonObject
        {
            ["system"] = OrganizationState.TaxIdSystem,
            ["value"] = taxId
        });

        // Add custom identifiers
        foreach (var (system, value) in _identifiers)
        {
            var identifier = new JsonObject
            {
                ["value"] = value
            };
            if (system is not null)
            {
                identifier["system"] = system;
            }
            identifiers.Add(identifier);
        }

        return identifiers;
    }

    private JsonArray BuildAddresses()
    {
        var addressArray = new JsonArray();

        foreach (var addr in _addresses)
        {
            var addressJson = new JsonObject
            {
                ["use"] = "work",
                ["type"] = "physical"
            };

            if (!string.IsNullOrEmpty(addr.Line))
            {
                addressJson["line"] = new JsonArray { addr.Line };
            }
            if (!string.IsNullOrEmpty(addr.City))
            {
                addressJson["city"] = addr.City;
            }
            if (!string.IsNullOrEmpty(addr.State))
            {
                addressJson["state"] = addr.State;
            }
            if (!string.IsNullOrEmpty(addr.PostalCode))
            {
                addressJson["postalCode"] = addr.PostalCode;
            }
            if (!string.IsNullOrEmpty(addr.Country))
            {
                addressJson["country"] = addr.Country;
            }

            addressArray.Add(addressJson);
        }

        return addressArray;
    }

    private JsonArray BuildTypes()
    {
        var typeArray = new JsonArray();

        foreach (var (system, code, display) in _typeCodes)
        {
            var coding = new JsonObject
            {
                ["code"] = code
            };

            if (system is not null)
            {
                coding["system"] = system;
            }
            else
            {
                coding["system"] = "http://terminology.hl7.org/CodeSystem/organization-type";
            }

            if (display is not null)
            {
                coding["display"] = display;
            }

            typeArray.Add(new JsonObject
            {
                ["coding"] = new JsonArray { coding }
            });
        }

        return typeArray;
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

    #region Factory Methods

    /// <summary>
    /// Creates a hospital organization builder.
    /// </summary>
    public static OrganizationBuilder Hospital(IFhirSchemaProvider schemaProvider, string? name = null)
    {
        var builder = Create(schemaProvider)
            .WithName(name ?? "General Hospital")
            .WithType("prov", display: "Healthcare Provider");
        return builder;
    }

    /// <summary>
    /// Creates a clinic organization builder.
    /// </summary>
    public static OrganizationBuilder Clinic(IFhirSchemaProvider schemaProvider, string? name = null)
    {
        var builder = Create(schemaProvider)
            .WithName(name ?? "Family Practice Clinic")
            .WithType("prov", display: "Healthcare Provider");
        return builder;
    }

    /// <summary>
    /// Creates an insurance company organization builder.
    /// </summary>
    public static OrganizationBuilder InsuranceCompany(IFhirSchemaProvider schemaProvider, string? name = null)
    {
        var builder = Create(schemaProvider)
            .WithName(name ?? "Health Insurance Co")
            .WithType("ins", display: "Insurance Company");
        return builder;
    }

    #endregion
}
