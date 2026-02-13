// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Tests.Fixtures;
using Ignixa.Anonymizer.Tests.Utilities;

namespace Ignixa.Anonymizer.Tests;

[Collection("Anonymizer Engine Collection")]
public class BundleAnonymizationTests
{
    private readonly AnonymizerEngineFixture _fixture;

    public BundleAnonymizationTests(AnonymizerEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenABundleResource_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("bundle-basic.json"),
            AnonymizerTestHelpers.ResourcePath("bundle-basic-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenAnonymizing_ThenResultContainsBundleResourceType()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await AnonymizerTestHelpers.AnonymizeFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("bundle-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldContain("\"resourceType\":\"Bundle\"");
    }

    [Fact]
    public async Task GivenABundleResource_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("bundle-basic.json"),
            AnonymizerTestHelpers.ResourcePath("bundle-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenSubstitute_ThenSubstitutedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("bundle-substitute.json"),
            AnonymizerTestHelpers.ResourcePath("bundle-substitute-target.json"));
    }

    [Fact]
    public async Task GivenABundleWithContainedInside_WhenAnonymizing_ThenContainedResourceIsAnonymized()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-in-bundle.json"),
            AnonymizerTestHelpers.ResourcePath("contained-in-bundle-target.json"));
    }

    [Fact]
    public async Task GivenABundleWithContainedInside_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-in-bundle.json"),
            AnonymizerTestHelpers.ResourcePath("contained-in-bundle-redact-all-target.json"));
    }
}
