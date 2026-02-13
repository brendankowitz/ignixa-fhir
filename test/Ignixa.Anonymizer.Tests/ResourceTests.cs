// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Tests.Fixtures;
using Ignixa.Anonymizer.Tests.Utilities;

namespace Ignixa.Anonymizer.Tests;

[Collection("Anonymizer Engine Collection")]
public class ResourceTests
{
    private readonly AnonymizerEngineFixture _fixture;

    public ResourceTests(AnonymizerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected()
    {
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            _fixture.R4CommonEngine,
            AnonymizerTestHelpers.ResourcePath("patient-basic.json"),
            AnonymizerTestHelpers.ResourcePath("patient-basic-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            _fixture.R4RedactAllEngine,
            AnonymizerTestHelpers.ResourcePath("patient-basic.json"),
            AnonymizerTestHelpers.ResourcePath("patient-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenResultContainsMetrics()
    {
        // Act
        var result = await AnonymizerTestHelpers.AnonymizeFromFileAsync(
            _fixture.R4CommonEngine,
            AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Metrics.ShouldNotBeNull();
        result.Value.Metrics.NodesProcessed.ShouldBeGreaterThan(0);
        result.Value.Metrics.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        result.Value.Metrics.OperationCounts.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenResultContainsResourceTypeInJson()
    {
        // Act
        var result = await AnonymizerTestHelpers.AnonymizeFromFileAsync(
            _fixture.R4CommonEngine,
            AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
        result.Value.AnonymizedJson.ShouldContain("\"resourceType\":\"Patient\"");
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenResultContainsResourceJsonNode()
    {
        // Act
        var result = await AnonymizerTestHelpers.AnonymizeFromFileAsync(
            _fixture.R4CommonEngine,
            AnonymizerTestHelpers.ResourcePath("patient-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Resource.ShouldNotBeNull();
        result.Value.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenAPatientResourceWithSpecialContents_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-special-content.json"),
            AnonymizerTestHelpers.ResourcePath("patient-special-content-target.json"));
    }

    [Fact(Skip = "Ignixa SDK bug: _birthDate without birthDate produces empty InstanceType on all children - https://github.com/brendankowitz/ignixa-fhir/issues/216")]
    public async Task GivenAPatientResourceWithNullDatetime_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-null-date.json"),
            AnonymizerTestHelpers.ResourcePath("patient-null-date-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithNoPartialRedactConfig_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("common-no-partial-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-no-partial.json"),
            AnonymizerTestHelpers.ResourcePath("patient-no-partial-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithPrimitiveSubstitute_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-primitive.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-substitute-primitive.json"),
            AnonymizerTestHelpers.ResourcePath("patient-substitute-primitive-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithComplexSubstitute_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-complex.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-substitute-complex.json"),
            AnonymizerTestHelpers.ResourcePath("patient-substitute-complex-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithConflictRules_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-conflict-rules.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-substitute-conflict-rules.json"),
            AnonymizerTestHelpers.ResourcePath("patient-substitute-conflict-rules-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithGeneralizeConfig_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("generalize-patient-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-generalize.json"),
            AnonymizerTestHelpers.ResourcePath("patient-generalize-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithMultipleSubstitute_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-substitute-multiple.json"),
            AnonymizerTestHelpers.ResourcePath("patient-substitute-multiple-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithMultipleSubstitute2_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-multiple-2.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("patient-substitute-multiple-2.json"),
            AnonymizerTestHelpers.ResourcePath("patient-substitute-multiple-2-target.json"));
    }
}
