// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class BundleDeIdTests
{
    private readonly DeIdEngineFixture _fixture;

    public BundleDeIdTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenABundleResource_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("bundle-basic.json"),
            DeIdTestHelpers.ResourcePath("bundle-basic-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenAnonymizing_ThenResultContainsBundleResourceType()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await DeIdTestHelpers.DeidentifyFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("bundle-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldContain("\"resourceType\":\"Bundle\"");
    }

    [Fact]
    public async Task GivenABundleResource_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("bundle-basic.json"),
            DeIdTestHelpers.ResourcePath("bundle-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenSubstitute_ThenSubstitutedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("bundle-substitute.json"),
            DeIdTestHelpers.ResourcePath("bundle-substitute-target.json"));
    }

    [Fact]
    public async Task GivenABundleWithContainedInside_WhenAnonymizing_ThenContainedResourceIsDeidentified()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-in-bundle.json"),
            DeIdTestHelpers.ResourcePath("contained-in-bundle-target.json"));
    }

    [Fact]
    public async Task GivenABundleWithContainedInside_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-in-bundle.json"),
            DeIdTestHelpers.ResourcePath("contained-in-bundle-redact-all-target.json"));
    }
}
