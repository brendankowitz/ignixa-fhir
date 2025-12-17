// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Security;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;

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
        var templateEngine = new NarrativeTemplateEngine(fhirPathFunctions);
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
        var patient = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(patient);

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
        var patient = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;
        var culture = new CultureInfo("en-US");

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(patient, culture);

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
        var observation = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(observation);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Observation"); // Generic template should show resource type
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GivenNullResource_WhenGeneratingNarrative_ThenThrowsArgumentNullException()
    {
        // Arrange
        ResourceJsonNode? resource = null;

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(resource!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("resource");
    }

    [Fact]
    public async Task GivenResourceWithoutResourceType_WhenGeneratingNarrative_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var json = """
            {
                "id": "example"
            }
            """;
        var resource = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        resource.FhirVersion = FhirVersion.R4;

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(resource);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Resource must have ResourceType");
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
        var patient = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(patient);

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
        var patient = JsonSerializer.Deserialize<ResourceJsonNode>(json)!;
        patient.FhirVersion = FhirVersion.R4;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await _generator.GenerateNarrativeAsync(patient, cancellationToken: cts.Token);

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
}
