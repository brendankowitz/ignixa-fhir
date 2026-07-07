// <copyright file="TerminologySeveritySemanticsTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Services;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Services;

/// <summary>
/// Tests for the three-outcome terminology severity semantics of <see cref="InMemoryTerminologyService"/>:
/// verified-in-valueset (valid), verified-not-in-valueset (Error), and unverifiable (non-failing Warning).
/// </summary>
public class TerminologySeveritySemanticsTests
{
    private const string AdministrativeGenderValueSet = "http://hl7.org/fhir/ValueSet/administrative-gender";
    private const string AdministrativeGenderSystem = "http://hl7.org/fhir/administrative-gender";

    private static InMemoryTerminologyService CreateService() =>
        new(new R4CoreSchemaProvider().ValueSetProvider);

    [Fact]
    public async Task GivenCodePresentWithMatchingSystem_WhenValidating_ThenReturnsValid()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: AdministrativeGenderSystem,
            code: "male",
            display: null,
            valueSetUrl: AdministrativeGenderValueSet,
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Information);
    }

    [Fact]
    public async Task GivenCodeAbsentButSystemEnumerated_WhenValidating_ThenReturnsError()
    {
        // Arrange - system IS enumerated by the local expansion, so the expansion is authoritative.
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: AdministrativeGenderSystem,
            code: "not-a-real-gender",
            display: null,
            valueSetUrl: AdministrativeGenderValueSet,
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Severity.ShouldBe(IssueSeverity.Error);
        result.Message.ShouldContain("was not found in the value set");
    }

    [Fact]
    public async Task GivenCodeWithExternalSystemNotEnumerated_WhenValidating_ThenReturnsNonFailingWarning()
    {
        // Arrange - SNOMED is not enumerated by the administrative-gender expansion, so membership is undecidable offline.
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://snomed.info/sct",
            code: "703118005",
            display: null,
            valueSetUrl: AdministrativeGenderValueSet,
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Warning);
        result.Message.ShouldContain("Unable to verify");
        result.Message.ShouldContain("not locally enumerable");
    }

    [Fact]
    public async Task GivenSystemlessCodeAbsent_WhenValidating_ThenReturnsError()
    {
        // Arrange - systemless (FHIR code-typed) bindings still error on genuinely-absent codes.
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: null,
            code: "not-a-real-gender",
            display: null,
            valueSetUrl: AdministrativeGenderValueSet,
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Severity.ShouldBe(IssueSeverity.Error);
        result.Message.ShouldContain("was not found in the value set");
    }

    [Fact]
    public async Task GivenSystemlessCodePresent_WhenValidating_ThenReturnsValid()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: null,
            code: "female",
            display: null,
            valueSetUrl: AdministrativeGenderValueSet,
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Information);
    }

    [Fact]
    public async Task GivenValueSetEntirelyAbsent_WhenValidating_ThenReturnsUnavailableWarning()
    {
        // Arrange - the "valueset entirely absent" path stays a non-failing Warning.
        var service = CreateService();

        // Act
        var result = await service.ValidateCodeAsync(
            system: "http://loinc.org",
            code: "8302-2",
            display: null,
            valueSetUrl: "http://custom.example.org/ValueSet/does-not-exist",
            CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Warning);
        result.Message.ShouldContain("unavailable");
    }
}
