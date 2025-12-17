// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Security;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator.Tests;

/// <summary>
/// Integration tests for <see cref="FhirNarrativeGenerator"/> orchestration.
/// </summary>
public class FhirNarrativeGeneratorTests
{
    private readonly FhirNarrativeGenerator _generator;
    private readonly IFhirSchemaProvider _schema;

    public FhirNarrativeGeneratorTests()
    {
        // Setup dependencies
        _schema = new R4CoreSchemaProvider();
        var templateResolver = new TemplateResolver();
        var fhirPathFunctions = new FhirPathScriptFunctions(_schema);
        var templateEngine = new NarrativeTemplateEngine(fhirPathFunctions, new MockStringLocalizer());
        var sanitizer = new XhtmlSanitizer();

        _generator = new FhirNarrativeGenerator(templateResolver, templateEngine, sanitizer);
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
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, patient.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Doe"); // Should contain family name
        narrative.Should().NotContain("<script"); // Should be sanitized
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
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, patient.FhirVersion ?? FhirVersion.R4, culture);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Smith");
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
        var narrative = await _generator.GenerateNarrativeAsync(element, observation.ResourceType, observation.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Observation"); // Generic template should show resource type
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GivenNullElement_WhenGeneratingNarrative_ThenThrowsArgumentNullException()
    {
        // Arrange
        IElement? element = null;

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(element!, "Patient", FhirVersion.R4);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("element");
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
        var act = async () => await _generator.GenerateNarrativeAsync(element, string.Empty, FhirVersion.R4);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("resourceType");
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
        var narrative = await _generator.GenerateNarrativeAsync(element, patient.ResourceType, patient.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotContain("<script");
        narrative.Should().NotContain("javascript:");
        narrative.Should().NotContain("onerror=");
        narrative.Should().NotContain("onclick=");
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
        var act = async () => await _generator.GenerateNarrativeAsync(element, patient.ResourceType, patient.FhirVersion ?? FhirVersion.R4, cancellationToken: cts.Token);

        // Assert
        // May either complete quickly before cancellation is observed, or throw
        try
        {
            var result = await act();
            result.Should().NotBeNull(); // Completed successfully
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
        var narrative = await _generator.GenerateNarrativeAsync(element, account.ResourceType, account.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Account");  // Resource type should be displayed in badge
        narrative.Should().Contain("fhir-account");  // CSS class should be present
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
        var narrative = await _generator.GenerateNarrativeAsync(element, claim.ResourceType, claim.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Claim");  // Resource type should be displayed in badge
        narrative.Should().Contain("fhir-claim");  // CSS class should be present
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
        var narrative = await _generator.GenerateNarrativeAsync(element, device.ResourceType, device.FhirVersion ?? FhirVersion.R4);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Device");  // Resource type should be displayed in badge
        narrative.Should().Contain("fhir-device");  // CSS class should be present
    }

    #endregion
}

internal class MockStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new LocalizedString(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
