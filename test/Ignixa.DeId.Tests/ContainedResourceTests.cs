// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class ContainedResourceTests
{
    private readonly DeIdEngineFixture _fixture;

    public ContainedResourceTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenAResourceWithContained_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-basic.json"),
            DeIdTestHelpers.ResourcePath("contained-basic-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenAnonymizing_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await DeIdTestHelpers.DeidentifyFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-basic.json"),
            DeIdTestHelpers.ResourcePath("contained-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenSubstitute_ThenSubstitutedJsonMatchesExpected()
    {
        var engine = DeIdTestHelpers.CreateR4Engine(DeIdTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath("contained-substitute.json"),
            DeIdTestHelpers.ResourcePath("contained-substitute-target.json"));
    }
}
