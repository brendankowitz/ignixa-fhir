// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Services;
using Xunit;

namespace Ignixa.Validation.Tests.Services;

/// <summary>
/// Tests for InMemoryTerminologyService.
/// </summary>
public class InMemoryTerminologyServiceTests
{
    #region Known ValueSets - Valid Codes

    [Fact]
    public async Task GivenValidGenderCode_WhenValidating_ThenReturnsValid()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "male",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(IssueSeverity.Information, result.Severity);
    }

    [Fact]
    public async Task GivenValidPublicationStatus_WhenValidating_ThenReturnsValid()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/publication-status",
            code: "active",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/publication-status",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(IssueSeverity.Information, result.Severity);
    }

    [Fact]
    public async Task GivenValidObservationStatus_WhenValidating_ThenReturnsValid()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/observation-status",
            code: "final",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/observation-status",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(IssueSeverity.Information, result.Severity);
    }

    #endregion

    #region Known ValueSets - Invalid Codes

    [Fact]
    public async Task GivenInvalidGenderCode_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "invalid-gender",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(IssueSeverity.Error, result.Severity);
        Assert.Contains("not found in the value set", result.Message);
    }

    [Fact]
    public async Task GivenInvalidCodeInFullyEnumeratedNonWellKnownSystem_WhenValidating_ThenReturnsError()
    {
        // Guards the `!EnumeratesSystem` widening: administrative-gender is a fully-enumerated, non
        // well-known system (not SNOMED/LOINC/UCUM). A genuinely-invalid code there is an
        // authoritative miss and must stay a hard Error, not degrade to a Warning.
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "definitely-not-a-gender",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(IssueSeverity.Error, result.Severity);
        Assert.Contains("not found in the value set", result.Message);
    }

    [Fact]
    public async Task GivenInvalidCode_WhenValidating_ThenIncludesValueSetInMessage()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "bad-code",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("http://hl7.org/fhir/ValueSet/administrative-gender", result.Message);
    }

    #endregion

    #region Unknown ValueSets - Graceful Degradation

    [Fact]
    public async Task GivenUnknownValueSet_WhenValidating_ThenReturnsWarning()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://snomed.info/sct",
            code: "123456",
            display: null,
            valueSetUrl: "http://custom.example.org/ValueSet/unknown-valueset",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid); // Graceful degradation - returns true
        Assert.Equal(IssueSeverity.Warning, result.Severity);
        Assert.Contains("Terminology validation unavailable", result.Message);
        Assert.Contains("http://custom.example.org/ValueSet/unknown-valueset", result.Message);
    }

    [Fact]
    public async Task GivenUnknownLoincValueSet_WhenValidating_ThenReturnsWarning()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://loinc.org",
            code: "8302-2",
            display: null,
            valueSetUrl: "http://custom.example.org/ValueSet/unknown-loinc-valueset",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid); // Graceful degradation
        Assert.Equal(IssueSeverity.Warning, result.Severity);
        Assert.Contains("unavailable", result.Message);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GivenNullCode_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: null,
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(IssueSeverity.Error, result.Severity);
        Assert.Contains("Code is required", result.Message);
    }

    [Fact]
    public async Task GivenEmptyCode_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: string.Empty,
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(IssueSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task GivenNullValueSetUrl_WhenValidating_ThenReturnsWarning()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/administrative-gender",
            code: "male",
            display: null,
            valueSetUrl: null,
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid); // Warning doesn't fail validation
        Assert.Equal(IssueSeverity.Warning, result.Severity);
        Assert.Contains("No ValueSet URL provided", result.Message);
    }

    [Fact]
    public async Task GivenNullSystem_WhenCodeIsValid_ThenReturnsValid()
    {
        // Arrange - system is optional for some bindings
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: null,
            code: "male",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Multiple ValueSets

    [Fact]
    public async Task GivenContactPointSystem_WhenCodeIsValid_ThenReturnsValid()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/contact-point-system",
            code: "email",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/contact-point-system",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task GivenNameUse_WhenCodeIsValid_ThenReturnsValid()
    {
        // Arrange
        var service = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://hl7.org/fhir/name-use",
            code: "official",
            display: null,
            valueSetUrl: "http://hl7.org/fhir/ValueSet/name-use",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
