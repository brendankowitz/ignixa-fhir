// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Tests.Fixtures;
using Ignixa.Anonymizer.Tests.Utilities;

namespace Ignixa.Anonymizer.Tests;

[Collection("Anonymizer Engine Collection")]
public class ContainedResourceTests
{
    private readonly AnonymizerEngineFixture _fixture;

    public ContainedResourceTests(AnonymizerEngineFixture fixture)
    {
        _fixture = fixture;
    }
    [Fact]
    public async Task GivenAResourceWithContained_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected()
    {
        var engine = _fixture.R4CommonEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-basic.json"),
            AnonymizerTestHelpers.ResourcePath("contained-basic-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenAnonymizing_ThenResultIsSuccess()
    {
        // Arrange
        var engine = _fixture.R4CommonEngine;

        // Act
        var result = await AnonymizerTestHelpers.AnonymizeFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-basic.json"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenRedactAll_ThenRedactedJsonMatchesExpected()
    {
        var engine = _fixture.R4RedactAllEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-basic.json"),
            AnonymizerTestHelpers.ResourcePath("contained-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenSubstitute_ThenSubstitutedJsonMatchesExpected()
    {
        var engine = AnonymizerTestHelpers.CreateR4Engine(AnonymizerTestHelpers.ConfigPath("substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath("contained-substitute.json"),
            AnonymizerTestHelpers.ResourcePath("contained-substitute-target.json"));
    }
}
