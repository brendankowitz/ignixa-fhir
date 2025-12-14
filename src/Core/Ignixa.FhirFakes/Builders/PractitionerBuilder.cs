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
/// Fluent builder for generating Practitioner resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// Example Usage:
/// <code>
/// var practitioner = PractitionerBuilder.Create(schemaProvider)
///     .WithName("Alice", "Anderson")
///     .WithNpi("1234567890")
///     .WithSpecialty("207Q00000X", display: "Family Medicine")
///     .WithTag(tag)
///     .Build();
///
/// // Or with individual name components:
/// var practitioner = PractitionerBuilder.Create(schemaProvider)
///     .WithGivenName("Alice")
///     .WithFamilyName("Anderson")
///     .WithIdentifier("12345", "http://example.org/identifiers")
///     .Build();
/// </code>
/// </remarks>
public sealed class PractitionerBuilder : FhirResourceBuilder<PractitionerBuilder>
{
    private string? _familyName;
    private string? _givenName;
    private readonly List<(string? System, string Value)> _identifiers = [];
    private readonly List<(string Code, string? System, string? Display)> _specialties = [];

    private PractitionerBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new PractitionerBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for generating base resources.</param>
    /// <returns>A new PractitionerBuilder instance</returns>
    public static PractitionerBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new PractitionerBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the practitioner's full name (given and family).
    /// </summary>
    /// <param name="given">The given (first) name</param>
    /// <param name="family">The family (last) name</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitioner = PractitionerBuilder.Create(schemaProvider)
    ///     .WithName("Alice", "Anderson")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerBuilder WithName(string given, string family)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(family);
        _givenName = given;
        _familyName = family;
        return this;
    }

    /// <summary>
    /// Sets the practitioner's family (last) name.
    /// </summary>
    /// <param name="family">The family name</param>
    /// <returns>This builder for method chaining</returns>
    public PractitionerBuilder WithFamilyName(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        _familyName = family;
        return this;
    }

    /// <summary>
    /// Sets the practitioner's given (first) name.
    /// </summary>
    /// <param name="given">The given name</param>
    /// <returns>This builder for method chaining</returns>
    public PractitionerBuilder WithGivenName(string given)
    {
        ArgumentNullException.ThrowIfNull(given);
        _givenName = given;
        return this;
    }

    /// <summary>
    /// Adds a National Provider Identifier (NPI) to the practitioner.
    /// </summary>
    /// <param name="npi">The NPI number (10 digits)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitioner = PractitionerBuilder.Create(schemaProvider)
    ///     .WithNpi("1234567890")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerBuilder WithNpi(string npi)
    {
        ArgumentNullException.ThrowIfNull(npi);
        _identifiers.Add(("http://hl7.org/fhir/sid/us-npi", npi));
        return this;
    }

    /// <summary>
    /// Adds a custom identifier to the practitioner.
    /// </summary>
    /// <param name="value">The identifier value</param>
    /// <param name="system">Optional identifier system URI</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitioner = PractitionerBuilder.Create(schemaProvider)
    ///     .WithIdentifier("12345", "http://hospital.example.org/staff-id")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerBuilder WithIdentifier(string value, string? system = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        _identifiers.Add((system, value));
        return this;
    }

    /// <summary>
    /// Adds a specialty to the practitioner's qualifications.
    /// </summary>
    /// <param name="code">The specialty code (e.g., "207Q00000X" for Family Medicine)</param>
    /// <param name="system">Optional code system (defaults to http://snomed.info/sct)</param>
    /// <param name="display">Optional display text (e.g., "Family Medicine")</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var practitioner = PractitionerBuilder.Create(schemaProvider)
    ///     .WithSpecialty("207Q00000X", "http://nucc.org/provider-taxonomy", "Family Medicine")
    ///     .WithSpecialty("419192003", display: "Internal Medicine")
    ///     .Build();
    /// </code>
    /// </example>
    public PractitionerBuilder WithSpecialty(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _specialties.Add((code, system ?? "http://snomed.info/sct", display));
        return this;
    }

    /// <summary>
    /// Builds the Practitioner resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Practitioner resource</returns>
    public override ResourceJsonNode Build()
    {
        var practitionerJson = new JsonObject
        {
            ["resourceType"] = "Practitioner",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["active"] = true
        };

        // Build name if provided
        if (!string.IsNullOrEmpty(_givenName) || !string.IsNullOrEmpty(_familyName))
        {
            practitionerJson["name"] = BuildName();
        }

        // Build identifiers
        if (_identifiers.Count > 0)
        {
            practitionerJson["identifier"] = BuildIdentifiers();
        }

        // Build qualifications (specialties)
        if (_specialties.Count > 0)
        {
            practitionerJson["qualification"] = BuildQualifications();
        }

        var json = practitionerJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    private JsonArray BuildName()
    {
        var nameJson = new JsonObject
        {
            ["use"] = "official"
        };

        if (!string.IsNullOrEmpty(_familyName))
        {
            nameJson["family"] = _familyName;
        }

        if (!string.IsNullOrEmpty(_givenName))
        {
            nameJson["given"] = new JsonArray { _givenName };
        }

        return new JsonArray { nameJson };
    }

    private JsonArray BuildIdentifiers()
    {
        var identifiers = new JsonArray();

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

    private JsonArray BuildQualifications()
    {
        var qualifications = new JsonArray();

        foreach (var (code, system, display) in _specialties)
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

            var qualification = new JsonObject
            {
                ["code"] = new JsonObject
                {
                    ["coding"] = new JsonArray { coding }
                }
            };

            qualifications.Add(qualification);
        }

        return qualifications;
    }
}
