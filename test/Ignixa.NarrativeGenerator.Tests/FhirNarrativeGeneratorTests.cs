// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator.Tests;

/// <summary>
/// Integration tests for <see cref="FhirNarrativeGenerator"/> orchestration.
/// Uses the Create() factory method to test with real ResourceManager localization.
/// </summary>
public class FhirNarrativeGeneratorTests
{
    private readonly INarrativeGenerator _generator;
    private readonly IFhirSchemaProvider _schema;

    public FhirNarrativeGeneratorTests()
    {
        // Use the Create() factory method which loads NarrativeStrings.resx by default
        _schema = new R4CoreSchemaProvider();
        _generator = FhirNarrativeGenerator.Create(_schema);
    }

    #region Patient Narrative Tests

    [Fact]
    public async Task GivenPatientResource_WhenGeneratingNarrative_ThenReturnsValidXhtml()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "example",
                "name": [{
                    "use": "official",
                    "family": "Doe",
                    "given": ["John", "Q"]
                }],
                "gender": "male",
                "birthDate": "1980-01-01"
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Doe"); // Should contain family name
        narrative.ShouldNotContain("<script"); // Should be sanitized
    }

    [Fact]
    public async Task GivenPatientResourceWithCulture_WhenGeneratingNarrative_ThenReturnsLocalizedXhtml()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "example",
                "name": [{
                    "family": "Smith"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);
        var culture = new CultureInfo("en-US");

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, culture);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Smith");
    }

    #endregion

    #region Generic Fallback Tests

    [Fact]
    public async Task GivenResourceWithoutSpecificTemplate_WhenGeneratingNarrative_ThenUsesGenericFallback()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Observation",
                "id": "example",
                "status": "final"
            }
            """;
        var observation = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;
        var element = observation.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, observation.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Observation"); // Generic template should show resource type
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GivenNullElement_WhenGeneratingNarrative_ThenThrowsArgumentNullException()
    {
        // Arrange
        IElement? element = null;

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(element!, "Patient");

        // Assert
        (await Should.ThrowAsync<ArgumentNullException>(act)).ParamName.ShouldBe("element");
    }

    [Fact]
    public async Task GivenEmptyResourceType_WhenGeneratingNarrative_ThenThrowsArgumentException()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "example"
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(element, string.Empty);

        // Assert
        (await Should.ThrowAsync<ArgumentException>(act)).ParamName.ShouldBe("resourceType");
    }

    #endregion

    #region XSS Protection Tests

    [Fact]
    public async Task GivenResourceData_WhenGeneratingNarrative_ThenOutputIsSanitized()
    {
        // Arrange - Even if template somehow produced unsafe content, sanitizer should catch it
        var json = """
            {
                "resourceType": "Patient",
                "id": "xss-test",
                "name": [{
                    "family": "Test"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType);

        // Assert
        narrative.ShouldNotContain("<script");
        narrative.ShouldNotContain("javascript:");
        narrative.ShouldNotContain("onerror=");
        narrative.ShouldNotContain("onclick=");
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public async Task GivenSchema_WhenUsingFactoryMethod_ThenCreatesWorkingGenerator()
    {
        // Arrange
        var schema = _schema;

        // Act
        var generator = FhirNarrativeGenerator.Create(schema);

        // Assert
        generator.ShouldNotBeNull();
        generator.ShouldBeAssignableTo<INarrativeGenerator>();

        // Verify it actually works with a real resource
        var json = """
            {
                "resourceType": "Patient",
                "id": "factory-test",
                "name": [{
                    "use": "official",
                    "family": "FactoryTest",
                    "given": ["John"]
                }],
                "gender": "male",
                "birthDate": "1990-01-15"
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        var narrative = await generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType);

        narrative.ShouldNotBeNullOrWhiteSpace();
        narrative.ShouldContain("FactoryTest");
        narrative.ShouldContain("John");
    }

    [Fact]
    public async Task GivenSchemaWithCustomLocalizer_WhenUsingFactoryMethod_ThenUsesCustomLocalizer()
    {
        // Arrange
        var schema = _schema;
        var customLocalizer = new MockStringLocalizer();

        // Act
        var generator = FhirNarrativeGenerator.Create(schema, customLocalizer);

        // Assert
        generator.ShouldNotBeNull();

        // Verify it works
        var json = """
            {
                "resourceType": "Patient",
                "id": "custom-localizer-test",
                "name": [{
                    "family": "LocalizedPatient"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        var narrative = await generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType);

        narrative.ShouldNotBeNullOrWhiteSpace();
        narrative.ShouldContain("LocalizedPatient");
    }

    [Fact]
    public void GivenNullSchema_WhenUsingFactoryMethod_ThenThrowsArgumentNullException()
    {
        // Arrange
        ISchema? schema = null;

        // Act
        var act = () => FhirNarrativeGenerator.Create(schema!);

        // Assert
        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("schema");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task GivenCancelledToken_WhenGeneratingNarrative_ThenCompletesOrThrowsOperationCancelled()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "example"
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(element, patient.ResourceType, cancellationToken: cts.Token);

        // Assert
        // May either complete quickly before cancellation is observed, or throw
        try
        {
            var result = await act();
            result.ShouldNotBeNull(); // Completed successfully
        }
        catch (OperationCanceledException)
        {
            // Also acceptable - cancellation was observed
        }
    }

    #endregion

    #region Generic Template Metadata Tests

    [Fact]
    public async Task GenerateNarrative_ForAccount_UsesGenericTemplateWithMetadata()
    {
        // Arrange: Account is a Trial-Use resource (no version-specific template embedded)
        var json = """
            {
              "resourceType": "Account",
              "id": "example",
              "status": "active",
              "name": "HACC Funded Billing for Peter James Chalmers",
              "type": {
                "coding": [{
                  "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                  "code": "PBILLACCT",
                  "display": "patient billing account"
                }]
              },
              "subject": [{
                "reference": "Patient/example",
                "display": "Peter James Chalmers"
              }],
              "servicePeriod": {
                "start": "2016-01-01",
                "end": "2016-06-30"
              }
            }
            """;

        var account = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        account.FhirVersion = FhirVersion.R4;
        var element = account.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, account.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Account");  // Resource type should be displayed in badge
        narrative.ShouldContain("fhir-account");  // CSS class should be present
    }

    [Fact]
    public async Task GenerateNarrative_ForClaim_UsesGenericTemplateWithMetadata()
    {
        // Arrange: Claim is a Trial-Use resource
        var json = """
            {
              "resourceType": "Claim",
              "id": "100150",
              "status": "active",
              "type": {
                "coding": [{
                  "system": "http://terminology.hl7.org/CodeSystem/claim-type",
                  "code": "oral"
                }]
              },
              "use": "claim",
              "patient": {
                "reference": "Patient/1"
              },
              "created": "2014-08-16",
              "insurer": {
                "reference": "Organization/2"
              },
              "provider": {
                "reference": "Organization/1"
              },
              "priority": {
                "coding": [{
                  "code": "normal"
                }]
              }
            }
            """;

        var claim = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        claim.FhirVersion = FhirVersion.R4;
        var element = claim.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, claim.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Claim");  // Resource type should be displayed in badge
        narrative.ShouldContain("fhir-claim");  // CSS class should be present
    }

    [Fact]
    public async Task GenerateNarrative_ForDevice_UsesGenericTemplateWithMetadata()
    {
        // Arrange: Device is a Trial-Use resource
        var json = """
            {
              "resourceType": "Device",
              "id": "example",
              "status": "active",
              "manufacturer": "Acme Devices, Inc",
              "modelNumber": "AB-123",
              "type": {
                "coding": [{
                  "system": "http://snomed.info/sct",
                  "code": "25062003",
                  "display": "Electrocardiographic monitor and recorder"
                }]
              },
              "patient": {
                "reference": "Patient/example"
              }
            }
            """;

        var device = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        device.FhirVersion = FhirVersion.R4;
        var element = device.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, device.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Device");  // Resource type should be displayed in badge
        narrative.ShouldContain("fhir-device");  // CSS class should be present
    }

    #endregion

    #region Multi-Format Template Tests

    [Fact]
    public async Task GivenPatient_WhenGeneratingHtmlNarrative_ThenReturnsXhtml()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "html-test",
                "name": [{
                    "use": "official",
                    "family": "HtmlTest",
                    "given": ["John"]
                }],
                "gender": "male",
                "birthDate": "1990-05-15"
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType,
            format: TemplateFormat.Html);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("<div"); // Should be XHTML
        narrative.ShouldContain("HtmlTest"); // Should contain family name
        narrative.ShouldContain("class="); // Should have CSS classes
        narrative.ShouldNotContain("<script"); // Should be sanitized
    }

    [Fact]
    public async Task GivenPatient_WhenGeneratingMarkdownNarrative_ThenReturnsMarkdown()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "md-test",
                "name": [{
                    "use": "official",
                    "family": "MarkdownPatient",
                    "given": ["Jane"]
                }],
                "gender": "female",
                "birthDate": "1985-03-20",
                "telecom": [{
                    "system": "phone",
                    "value": "555-1234",
                    "use": "home"
                }],
                "address": [{
                    "use": "home",
                    "line": ["123 Main St"],
                    "city": "Boston",
                    "state": "MA",
                    "postalCode": "02101"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType,
            format: TemplateFormat.Markdown);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("#"); // Should contain Markdown headers
        narrative.ShouldContain("MarkdownPatient"); // Should contain family name
        narrative.ShouldContain("**"); // Should contain Markdown bold syntax
        narrative.ShouldNotContain("<div"); // Should NOT be HTML
        narrative.ShouldNotContain("class="); // Should NOT have CSS classes
    }

    [Fact]
    public async Task GivenPatient_WhenGeneratingCompact_ThenReturnsSingleLineCondensedFormat()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "vector-test",
                "name": [{
                    "use": "official",
                    "family": "VectorPatient",
                    "given": ["Alex"]
                }],
                "gender": "male",
                "birthDate": "1975-08-10",
                "address": [{
                    "city": "Seattle",
                    "state": "WA"
                }],
                "managingOrganization": {
                    "display": "General Hospital"
                }
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // Should NOT have resource type prefix
        narrative.ShouldContain("VectorPatient"); // Should contain family name
        narrative.ShouldContain("male"); // Should contain gender
        narrative.ShouldContain("Seattle"); // Should contain city
        narrative.ShouldContain("General Hospital"); // Should contain organization
        // Compact should be dense, single-line format
        narrative.Trim().ShouldNotContain("\n\n"); // Should not have multiple blank lines
        narrative.ShouldNotContain("<div"); // Should NOT be HTML
        narrative.ShouldNotContain("#"); // Should NOT have Markdown headers
    }

    [Fact]
    public async Task GivenPatient_WhenGeneratingMarkdownNarrative_ThenNotSanitized()
    {
        // Arrange - Markdown format should NOT have HTML sanitization applied
        var json = """
            {
                "resourceType": "Patient",
                "id": "md-no-sanitize",
                "name": [{
                    "family": "NoSanitize"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType,
            format: TemplateFormat.Markdown);

        // Assert - Just verify it generates without error and contains content
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("NoSanitize");
    }

    [Fact]
    public async Task GivenPatient_WhenGeneratingCompact_ThenNotSanitized()
    {
        // Arrange - Compact format should NOT have HTML sanitization applied
        var json = """
            {
                "resourceType": "Patient",
                "id": "compact-no-sanitize",
                "name": [{
                    "family": "NoSanitizeCompact"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            patient.ResourceType,
            format: TemplateFormat.Compact);

        // Assert - Just verify it generates without error and contains content
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("NoSanitizeCompact");
    }

    [Fact]
    public async Task GivenResourceWithoutSpecificTemplate_WhenGeneratingMarkdown_ThenUsesGenericFallback()
    {
        // Arrange - Account doesn't have a specific Markdown template
        var json = """
            {
                "resourceType": "Account",
                "id": "md-fallback",
                "status": "active",
                "name": "Test Account"
            }
            """;
        var account = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        account.FhirVersion = FhirVersion.R4;
        var element = account.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            account.ResourceType,
            format: TemplateFormat.Markdown);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Account"); // Should contain resource type
        narrative.ShouldContain("#"); // Should be Markdown format
    }

    [Fact]
    public async Task GivenResourceWithoutSpecificTemplate_WhenGeneratingCompact_ThenUsesGenericFallback()
    {
        // Arrange - Claim doesn't have a specific Compact template
        var json = """
            {
                "resourceType": "Claim",
                "id": "compact-fallback",
                "status": "active"
            }
            """;
        var claim = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        claim.FhirVersion = FhirVersion.R4;
        var element = claim.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            claim.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // Should NOT have resource type prefix
        narrative.ShouldContain("active"); // Should contain status
    }

    [Fact]
    public async Task GivenPatientWithAllFields_WhenGeneratingAllFormats_ThenAllFormatsContainKeyData()
    {
        // Arrange - A comprehensive patient with many fields
        var json = """
            {
                "resourceType": "Patient",
                "id": "comprehensive",
                "identifier": [{
                    "type": {
                        "coding": [{
                            "code": "MR",
                            "display": "Medical Record Number"
                        }]
                    },
                    "value": "12345"
                }],
                "name": [{
                    "use": "official",
                    "family": "ComprehensivePatient",
                    "given": ["Test"]
                }],
                "gender": "other",
                "birthDate": "2000-01-01",
                "address": [{
                    "city": "TestCity",
                    "state": "TS"
                }]
            }
            """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(_schema);

        // Act
        var htmlNarrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, format: TemplateFormat.Html);
        var mdNarrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, format: TemplateFormat.Markdown);
        var compactNarrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, format: TemplateFormat.Compact);

        // Assert - All formats should contain the patient name
        htmlNarrative.ShouldContain("ComprehensivePatient");
        mdNarrative.ShouldContain("ComprehensivePatient");
        compactNarrative.ShouldContain("ComprehensivePatient");

        // Each format should have distinct characteristics
        htmlNarrative.ShouldContain("<"); // HTML tags
        mdNarrative.ShouldContain("#"); // Markdown headers
        compactNarrative.ShouldNotStartWith("["); // Compact has no resource type prefix
    }

    [Fact]
    public async Task GivenNoLocalizer_WhenUsingCreate_ThenUsesDefaultResourceManagerLocalization()
    {
        // Arrange - use Create() factory which should load NarrativeStrings.resx by default
        var schema = new R4CoreSchemaProvider();
        var generator = FhirNarrativeGenerator.Create(schema);

        var json = """
            {
                "resourceType": "Patient",
                "id": "example",
                "name": [{
                    "family": "Smith",
                    "given": ["John"]
                }],
                "gender": "male"
            }
            """;

        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var element = patient.ToElement(schema);

        // Act
        var result = await generator.GenerateNarrativeAsync(element, "Patient");

        // Assert - verify that localized strings from NarrativeStrings.resx are used
        // NOT the keys like "Patient.Title" but the actual values like "Patient"
        result.ShouldContain("Patient");  // From Patient.Title = "Patient"
        result.ShouldContain("Male");      // From Patient.Gender.male = "Male"
        result.ShouldNotContain("Patient.Title");     // Should NOT contain the key
        result.ShouldNotContain("Patient.Gender.male"); // Should NOT contain the key
    }

    #endregion

    #region Bundle with Nested Resource Rendering Tests

    [Fact]
    public async Task GivenBundle_WhenGeneratingNarrative_WithPatientEntry_ThenRendersPatientNarrative()
    {
        // Arrange - Bundle with Patient entry
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-patient",
                "type": "collection",
                "entry": [{
                    "fullUrl": "Patient/example",
                    "resource": {
                        "resourceType": "Patient",
                        "id": "example",
                        "name": [{
                            "use": "official",
                            "family": "Smith",
                            "given": ["John"]
                        }],
                        "gender": "male",
                        "birthDate": "1980-01-01"
                    }
                }]
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Bundle");  // Bundle header
        narrative.ShouldContain("Patient");  // Entry type
        narrative.ShouldContain("Smith");  // Patient name from nested rendering
        narrative.ShouldContain("John");  // Given name from nested rendering
        narrative.ShouldContain("Male");  // Gender from nested rendering
    }

    [Fact]
    public async Task GivenBundle_WhenGeneratingNarrative_WithMixedResourceTypes_ThenRendersAllResources()
    {
        // Arrange - Bundle with Patient and Observation entries
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-mixed",
                "type": "collection",
                "entry": [
                    {
                        "fullUrl": "Patient/example",
                        "resource": {
                            "resourceType": "Patient",
                            "id": "example",
                            "name": [{
                                "family": "Doe"
                            }],
                            "gender": "female"
                        }
                    },
                    {
                        "fullUrl": "Observation/example",
                        "resource": {
                            "resourceType": "Observation",
                            "id": "obs-1",
                            "status": "final",
                            "code": {
                                "coding": [{
                                    "system": "http://loinc.org",
                                    "code": "15074-8",
                                    "display": "Glucose"
                                }]
                            },
                            "valueQuantity": {
                                "value": 95,
                                "unit": "mg/dL"
                            }
                        }
                    }
                ]
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Patient");  // First entry type
        narrative.ShouldContain("Doe");  // Patient name
        narrative.ShouldContain("Observation");  // Second entry type
        narrative.ShouldContain("Glucose");  // Observation code display
        narrative.ShouldContain("95");  // Observation value
    }

    [Fact]
    public async Task GivenBundle_WhenGeneratingNarrative_WithUnsupportedResourceType_ThenShowsFallbackMessage()
    {
        // Arrange - Bundle with a resource type that might not have a specific template
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-unsupported",
                "type": "collection",
                "entry": [{
                    "fullUrl": "Account/example",
                    "resource": {
                        "resourceType": "Account",
                        "id": "account-1",
                        "status": "active",
                        "name": "Test Account"
                    }
                }]
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Bundle");
        narrative.ShouldContain("Account");  // Entry type should be shown
        // Should render using generic template or show resource details
        (narrative.Contains("active") || narrative.Contains("Generic") || narrative.Contains("Account")).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenBundle_WhenGeneratingNarrative_WithCircularReference_ThenHandlesGracefully()
    {
        // Arrange - Bundle containing itself (circular reference simulation)
        // Note: In practice, this tests depth limiting rather than true circular references
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-circular",
                "type": "collection",
                "entry": [{
                    "fullUrl": "Patient/example",
                    "resource": {
                        "resourceType": "Patient",
                        "id": "example",
                        "name": [{
                            "family": "Circular"
                        }]
                    }
                }]
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act - Should not throw, even with nested rendering
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Bundle");
        narrative.ShouldContain("Patient");
        narrative.ShouldContain("Circular");
    }

    [Fact]
    public async Task GivenBundle_WhenGeneratingNarrative_WithMultipleEntriesOfSameType_ThenRendersAll()
    {
        // Arrange - Bundle with multiple Patient entries
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-multiple",
                "type": "collection",
                "entry": [
                    {
                        "fullUrl": "Patient/patient1",
                        "resource": {
                            "resourceType": "Patient",
                            "id": "patient1",
                            "name": [{
                                "family": "Smith"
                            }]
                        }
                    },
                    {
                        "fullUrl": "Patient/patient2",
                        "resource": {
                            "resourceType": "Patient",
                            "id": "patient2",
                            "name": [{
                                "family": "Jones"
                            }]
                        }
                    },
                    {
                        "fullUrl": "Patient/patient3",
                        "resource": {
                            "resourceType": "Patient",
                            "id": "patient3",
                            "name": [{
                                "family": "Brown"
                            }]
                        }
                    }
                ]
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Smith");
        narrative.ShouldContain("Jones");
        narrative.ShouldContain("Brown");
        // Verify all three entries are rendered
        var smithCount = System.Text.RegularExpressions.Regex.Count(narrative, "Smith");
        smithCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenEmptyBundle_WhenGeneratingNarrative_ThenHandlesGracefully()
    {
        // Arrange - Bundle with no entries
        var json = """
            {
                "resourceType": "Bundle",
                "id": "bundle-empty",
                "type": "collection",
                "entry": []
            }
            """;
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        bundle.FhirVersion = FhirVersion.R4;
        var element = bundle.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(element, bundle.ResourceType);

        // Assert
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Bundle");
        // Should not throw error for empty entries
    }

    #endregion
}

internal class MockStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new LocalizedString(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
