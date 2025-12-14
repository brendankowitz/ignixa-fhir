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
/// Fluent builder for generating Observation resources with extended features.
/// Supports quantity values, coded values, timing, categories, identifiers, and performers.
/// </summary>
/// <remarks>
/// <para>
/// This builder provides comprehensive support for creating test observations with:
/// </para>
/// <list type="bullet">
/// <item><description>Quantity values (valueQuantity) for numeric observations</description></item>
/// <item><description>Coded values (valueCodeableConcept) for categorical observations</description></item>
/// <item><description>Timing (effectiveDateTime or effectivePeriod)</description></item>
/// <item><description>Categories (vital-signs, laboratory, etc.)</description></item>
/// <item><description>Identifiers for test isolation and case-sensitive searches</description></item>
/// <item><description>Performers (Practitioner or Organization references)</description></item>
/// </list>
/// <para><strong>Example Usage - Basic Quantity Observation:</strong></para>
/// <code>
/// var obs = ObservationBuilder.Create(schemaProvider)
///     .WithCode("29463-7", "http://loinc.org", "Body Weight")
///     .WithQuantityValue(185, "[lb_av]")
///     .WithSubject(patientId)
///     .WithTag(tag)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Coded Value Observation:</strong></para>
/// <code>
/// var obs = ObservationBuilder.Create(schemaProvider)
///     .WithCode("883-9", "http://loinc.org", "ABO group")
///     .WithCodedValue("A", "http://snomed.info/sct", "Blood group A")
///     .WithSubject(patientId)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Date Range with Performer:</strong></para>
/// <code>
/// var obs = ObservationBuilder.Create(schemaProvider)
///     .WithCode("4548-4", "http://loinc.org", "Hemoglobin A1c")
///     .WithQuantityValue(7.5m, "%")
///     .WithEffectivePeriod("1980-05-16", "1980-05-17")
///     .WithCategory("laboratory")
///     .WithPractitionerPerformer(practitionerId)
///     .WithSubject(patientId)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Composite Search Testing:</strong></para>
/// <code>
/// var obs = ObservationBuilder.Create(schemaProvider)
///     .WithCode("85354-9", "http://loinc.org", "Blood pressure")
///     .WithQuantityValue(120, "mmHg", "http://unitsofmeasure.org")
///     .WithIdentifier("OBS-12345", "http://hospital.example.org")
///     .WithCategory("vital-signs", "http://terminology.hl7.org/CodeSystem/observation-category")
///     .Build();
/// </code>
/// </remarks>
public sealed class ObservationBuilder : FhirResourceBuilder<ObservationBuilder>
{
    // Core fields
    private string _status = "final";

    // Code
    private string? _codeCode;
    private string? _codeSystem;
    private string? _codeDisplay;

    // Timing (mutually exclusive)
    private string? _effectiveDateTime;
    private string? _effectivePeriodStart;
    private string? _effectivePeriodEnd;

    // Value - Quantity variant
    private decimal? _valueQuantity;
    private string? _valueQuantityUnit;
    private string? _valueQuantitySystem;

    // Value - CodeableConcept variant (mutually exclusive with quantity)
    private readonly List<(string Code, string? System, string? Display)> _valueCodeableConceptCodings = [];
    private string? _valueCodeableConceptText;
    private bool _emptyValueCodeableConcept;

    // Category
    private string? _categoryCode;
    private string? _categorySystem;
    private string? _categoryDisplay;

    // Subject
    private string? _subjectReference;

    // Identifiers
    private readonly List<(string? System, string Value)> _identifiers = [];

    // Performers
    private readonly List<(string ResourceType, string Id)> _performers = [];

    private ObservationBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new ObservationBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
    /// <returns>A new ObservationBuilder instance.</returns>
    /// <example>
    /// <code>
    /// var builder = ObservationBuilder.Create(schemaProvider);
    /// </code>
    /// </example>
    public static ObservationBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new ObservationBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the observation code using LOINC or other coding system.
    /// </summary>
    /// <param name="code">The code value (e.g., "29463-7" for body weight).</param>
    /// <param name="system">The code system (e.g., "http://loinc.org").</param>
    /// <param name="display">Optional display text for the code.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithCode("29463-7", "http://loinc.org", "Body Weight")
    /// </code>
    /// </example>
    public ObservationBuilder WithCode(string code, string system, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(system);
        _codeCode = code;
        _codeSystem = system;
        _codeDisplay = display;
        return this;
    }

    /// <summary>
    /// Sets the observation status.
    /// Default is "final" if not specified.
    /// </summary>
    /// <param name="status">The status code (e.g., "final", "preliminary", "amended").</param>
    /// <returns>This builder for method chaining.</returns>
    public ObservationBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the subject reference (typically a Patient).
    /// </summary>
    /// <param name="patientId">The patient resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithSubject(patient.Id!)
    /// // Generates: "subject": { "reference": "Patient/{id}" }
    /// </code>
    /// </example>
    public ObservationBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectReference = $"Patient/{patientId}";
        return this;
    }

    /// <summary>
    /// Sets the observation value as a quantity (valueQuantity).
    /// Use this for numeric observations like weight, height, blood pressure.
    /// Mutually exclusive with <see cref="WithCodedValue"/>.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of measure (e.g., "kg", "[lb_av]", "mmHg").</param>
    /// <param name="system">Optional code system for the unit (defaults to http://unitsofmeasure.org).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Weight in pounds
    /// .WithQuantityValue(185, "[lb_av]")
    ///
    /// // Blood pressure in mmHg with explicit system
    /// .WithQuantityValue(120, "mmHg", "http://unitsofmeasure.org")
    /// </code>
    /// </example>
    public ObservationBuilder WithQuantityValue(decimal value, string unit, string? system = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _valueQuantity = value;
        _valueQuantityUnit = unit;
        _valueQuantitySystem = system ?? "http://unitsofmeasure.org";

        // Clear coded value variant (mutually exclusive)
        _valueCodeableConceptCodings.Clear();
        _valueCodeableConceptText = null;
        _emptyValueCodeableConcept = false;

        return this;
    }

    /// <summary>
    /// Sets the observation value as a codeable concept (valueCodeableConcept).
    /// Use this for categorical observations like blood type, smoking status.
    /// Mutually exclusive with <see cref="WithQuantityValue"/>.
    /// Can be called multiple times to add multiple codings to the same valueCodeableConcept.
    /// </summary>
    /// <param name="code">The code value.</param>
    /// <param name="system">Optional code system URI.</param>
    /// <param name="display">Optional display text for the code.</param>
    /// <param name="text">Optional free-text representation (shared across all codings).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Single coding with text
    /// .WithCodedValue("A", "http://snomed.info/sct", "Blood group A")
    ///
    /// // Multiple codings in one valueCodeableConcept
    /// .WithCodedValue("code1", "system1")
    /// .WithCodedValue("code2", "system2", "Display 2", "text")
    /// </code>
    /// </example>
    public ObservationBuilder WithCodedValue(string code, string? system = null, string? display = null, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _valueCodeableConceptCodings.Add((code, system, display));

        if (text is not null)
        {
            _valueCodeableConceptText = text;
        }

        // Clear quantity variant and empty flag (mutually exclusive)
        _valueQuantity = null;
        _valueQuantityUnit = null;
        _valueQuantitySystem = null;
        _emptyValueCodeableConcept = false;

        return this;
    }

    /// <summary>
    /// Sets the valueCodeableConcept text field without any codings.
    /// Use this for text-only coded values (no system or code).
    /// Mutually exclusive with <see cref="WithQuantityValue"/>.
    /// </summary>
    /// <param name="text">The text-only representation.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Text-only coded value (no codings)
    /// .WithTextOnlyCodedValue("text description")
    /// </code>
    /// </example>
    public ObservationBuilder WithTextOnlyCodedValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _valueCodeableConceptText = text;
        _valueCodeableConceptCodings.Clear();
        _emptyValueCodeableConcept = false;

        // Clear quantity variant (mutually exclusive)
        _valueQuantity = null;
        _valueQuantityUnit = null;
        _valueQuantitySystem = null;

        return this;
    }

    /// <summary>
    /// Sets an empty valueCodeableConcept (for testing :not modifier over missing values).
    /// Mutually exclusive with <see cref="WithQuantityValue"/> and <see cref="WithCodedValue"/>.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // For testing :not searches over missing values
    /// .WithEmptyCodedValue()
    /// .WithCategory("laboratory")
    /// </code>
    /// </example>
    public ObservationBuilder WithEmptyCodedValue()
    {
        _emptyValueCodeableConcept = true;
        _valueCodeableConceptCodings.Clear();
        _valueCodeableConceptText = null;

        // Clear quantity variant (mutually exclusive)
        _valueQuantity = null;
        _valueQuantityUnit = null;
        _valueQuantitySystem = null;

        return this;
    }

    /// <summary>
    /// Sets the effective date/time (effectiveDateTime) for when the observation was made.
    /// Mutually exclusive with <see cref="WithEffectivePeriod"/>.
    /// </summary>
    /// <param name="dateTime">ISO 8601 date/time string (e.g., "2023-01-15T10:30:00Z").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithEffectiveDateTime("2023-01-15T10:30:00Z")
    /// </code>
    /// </example>
    public ObservationBuilder WithEffectiveDateTime(string dateTime)
    {
        ArgumentNullException.ThrowIfNull(dateTime);
        _effectiveDateTime = dateTime;
        _effectivePeriodStart = null;
        _effectivePeriodEnd = null;
        return this;
    }

    /// <summary>
    /// Sets the effective period (effectivePeriod) for observations that span a time range.
    /// Mutually exclusive with <see cref="WithEffectiveDateTime"/>.
    /// </summary>
    /// <param name="startDate">ISO 8601 date/time string for period start.</param>
    /// <param name="endDate">ISO 8601 date/time string for period end.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithEffectivePeriod("1980-05-16", "1980-05-17")
    /// </code>
    /// </example>
    public ObservationBuilder WithEffectivePeriod(string startDate, string endDate)
    {
        ArgumentNullException.ThrowIfNull(startDate);
        ArgumentNullException.ThrowIfNull(endDate);
        _effectivePeriodStart = startDate;
        _effectivePeriodEnd = endDate;
        _effectiveDateTime = null;
        return this;
    }

    /// <summary>
    /// Sets the observation category (e.g., "vital-signs", "laboratory").
    /// </summary>
    /// <param name="code">The category code.</param>
    /// <param name="system">Optional code system (defaults to http://terminology.hl7.org/CodeSystem/observation-category).</param>
    /// <param name="display">Optional display text.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithCategory("vital-signs")
    /// .WithCategory("laboratory", "http://terminology.hl7.org/CodeSystem/observation-category", "Laboratory")
    /// </code>
    /// </example>
    public ObservationBuilder WithCategory(string code, string? system = null, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _categoryCode = code;
        _categorySystem = system ?? "http://terminology.hl7.org/CodeSystem/observation-category";
        _categoryDisplay = display;
        return this;
    }

    /// <summary>
    /// Adds an identifier to the observation.
    /// Can be called multiple times to add multiple identifiers.
    /// Useful for testing case-sensitive token searches and :missing modifier.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <param name="system">Optional identifier system URI.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Test case-sensitive searches
    /// .WithIdentifier("VALUE", "test")
    /// .WithIdentifier("value", "test")
    ///
    /// // Hospital identifier
    /// .WithIdentifier("OBS-12345", "http://hospital.example.org")
    /// </code>
    /// </example>
    public ObservationBuilder WithIdentifier(string value, string? system = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        _identifiers.Add((system, value));
        return this;
    }

    /// <summary>
    /// Adds a performer reference (who performed the observation).
    /// Can be called multiple times for multiple performers.
    /// </summary>
    /// <param name="resourceType">The resource type (e.g., "Practitioner", "Organization").</param>
    /// <param name="id">The resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithPerformer("Practitioner", practitionerId)
    /// .WithPerformer("Organization", labOrgId)
    /// </code>
    /// </example>
    public ObservationBuilder WithPerformer(string resourceType, string id)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);
        _performers.Add((resourceType, id));
        return this;
    }

    /// <summary>
    /// Adds a Practitioner performer (convenience method).
    /// Equivalent to WithPerformer("Practitioner", practitionerId).
    /// </summary>
    /// <param name="practitionerId">The practitioner resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithPractitionerPerformer(practitioner.Id!)
    /// </code>
    /// </example>
    public ObservationBuilder WithPractitionerPerformer(string practitionerId)
    {
        return WithPerformer("Practitioner", practitionerId);
    }

    /// <summary>
    /// Adds an Organization performer (convenience method).
    /// Equivalent to WithPerformer("Organization", organizationId).
    /// </summary>
    /// <param name="organizationId">The organization resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithOrganizationPerformer(labOrganization.Id!)
    /// </code>
    /// </example>
    public ObservationBuilder WithOrganizationPerformer(string organizationId)
    {
        return WithPerformer("Organization", organizationId);
    }

    /// <summary>
    /// Builds the Observation resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Observation resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if required fields (code, value) are not set.</exception>
    /// <example>
    /// <code>
    /// var observation = ObservationBuilder.Create(schemaProvider)
    ///     .WithCode("29463-7", "http://loinc.org", "Body Weight")
    ///     .WithQuantityValue(185, "[lb_av]")
    ///     .WithSubject(patientId)
    ///     .Build();
    /// </code>
    /// </example>
    public override ResourceJsonNode Build()
    {
        var obsJson = new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status
        };

        // Code is required
        if (_codeCode is null || _codeSystem is null)
        {
            throw new InvalidOperationException("Observation code is required. Call WithCode() before Build().");
        }

        obsJson["code"] = CreateCodeableConcept(_codeCode, _codeSystem, _codeDisplay);

        // Subject
        if (_subjectReference is not null)
        {
            obsJson["subject"] = CreateReference(_subjectReference);
        }

        // Category
        if (_categoryCode is not null)
        {
            obsJson["category"] = new JsonArray
            {
                CreateCodeableConcept(_categoryCode, _categorySystem!, _categoryDisplay)
            };
        }

        // Effective timing (mutually exclusive)
        if (_effectivePeriodStart is not null && _effectivePeriodEnd is not null)
        {
            obsJson["effectivePeriod"] = new JsonObject
            {
                ["start"] = _effectivePeriodStart,
                ["end"] = _effectivePeriodEnd
            };
        }
        else if (_effectiveDateTime is not null)
        {
            obsJson["effectiveDateTime"] = _effectiveDateTime;
        }

        // Value (mutually exclusive)
        if (_emptyValueCodeableConcept)
        {
            // Empty coded value (for :not modifier testing)
            obsJson["valueCodeableConcept"] = new JsonObject();
        }
        else if (_valueCodeableConceptCodings.Count > 0)
        {
            // Coded value with one or more codings
            obsJson["valueCodeableConcept"] = CreateCodeableConceptWithCodings(
                _valueCodeableConceptCodings,
                _valueCodeableConceptText);
        }
        else if (_valueCodeableConceptText is not null)
        {
            // Text-only coded value (no codings)
            obsJson["valueCodeableConcept"] = new JsonObject
            {
                ["text"] = _valueCodeableConceptText
            };
        }
        else if (_valueQuantity.HasValue)
        {
            // Quantity value
            obsJson["valueQuantity"] = new JsonObject
            {
                ["value"] = _valueQuantity.Value,
                ["unit"] = _valueQuantityUnit,
                ["system"] = _valueQuantitySystem,
                ["code"] = _valueQuantityUnit
            };
        }

        // Identifiers
        if (_identifiers.Count > 0)
        {
            obsJson["identifier"] = BuildIdentifiers();
        }

        // Performers
        if (_performers.Count > 0)
        {
            obsJson["performer"] = BuildPerformers();
        }

        var json = obsJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    private static JsonObject CreateReference(string reference)
    {
        return new JsonObject
        {
            ["reference"] = reference
        };
    }

    private static JsonObject CreateCodeableConceptWithCodings(List<(string Code, string? System, string? Display)> codings, string? text = null)
    {
        var codingsArray = new JsonArray();

        foreach (var (code, system, display) in codings)
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

            codingsArray.Add(coding);
        }

        var concept = new JsonObject
        {
            ["coding"] = codingsArray
        };

        if (text is not null)
        {
            concept["text"] = text;
        }

        return concept;
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

    private JsonArray BuildPerformers()
    {
        var performers = new JsonArray();

        foreach (var (resourceType, id) in _performers)
        {
            performers.Add(new JsonObject
            {
                ["reference"] = $"{resourceType}/{id}"
            });
        }

        return performers;
    }
}
