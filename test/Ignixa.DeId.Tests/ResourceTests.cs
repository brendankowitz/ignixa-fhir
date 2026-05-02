// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class ResourceTests
{
    private readonly DeIdEngineFixture _fixture;

    public ResourceTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected()
    {
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            _fixture.R4CommonEngine,
            DeIdTestHelpers.ResourcePath("patient-basic.json"),
            DeIdTestHelpers.ResourcePath("patient-basic-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            _fixture.R4RedactAllEngine,
            DeIdTestHelpers.ResourcePath("patient-basic.json"),
            DeIdTestHelpers.ResourcePath("patient-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenResultContainsMetrics()
    {
        // Act
        var result = await DeIdTestHelpers.DeidentifyFromFileAsync(
            _fixture.R4CommonEngine,
            DeIdTestHelpers.ResourcePath("patient-basic.json"));

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
        var result = await DeIdTestHelpers.DeidentifyFromFileAsync(
            _fixture.R4CommonEngine,
            DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotBeNullOrEmpty();
        result.Value.DeidentifiedJson.ShouldContain("\"resourceType\":\"Patient\"");
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenResultContainsResourceJsonNode()
    {
        // Act
        var result = await DeIdTestHelpers.DeidentifyFromFileAsync(
            _fixture.R4CommonEngine,
            DeIdTestHelpers.ResourcePath("patient-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Resource.ShouldNotBeNull();
        result.Value.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenAPatientResourceWithSpecialContents_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-special-content.json"),
            DeIdTestHelpers.ResourcePath("patient-special-content-target.json"));
    }

    [Fact(Skip = "Ignixa SDK bug: _birthDate without birthDate produces empty InstanceType on all children - https://github.com/brendankowitz/ignixa-fhir/issues/216")]
    public async Task GivenAPatientResourceWithNullDatetime_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-null-date.json"),
            DeIdTestHelpers.ResourcePath("patient-null-date-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithNoPartialRedactConfig_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("common-no-partial-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-no-partial.json"),
            DeIdTestHelpers.ResourcePath("patient-no-partial-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithPrimitiveSubstitute_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-primitive.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-substitute-primitive.json"),
            DeIdTestHelpers.ResourcePath("patient-substitute-primitive-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithComplexSubstitute_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-complex.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-substitute-complex.json"),
            DeIdTestHelpers.ResourcePath("patient-substitute-complex-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithConflictRules_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-conflict-rules.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-substitute-conflict-rules.json"),
            DeIdTestHelpers.ResourcePath("patient-substitute-conflict-rules-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithGeneralizeConfig_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("generalize-patient-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-generalize.json"),
            DeIdTestHelpers.ResourcePath("patient-generalize-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithMultipleSubstitute_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-substitute-multiple.json"),
            DeIdTestHelpers.ResourcePath("patient-substitute-multiple-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithMultipleSubstitute2_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-multiple-2.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("patient-substitute-multiple-2.json"),
            DeIdTestHelpers.ResourcePath("patient-substitute-multiple-2-target.json"));
    }
}
