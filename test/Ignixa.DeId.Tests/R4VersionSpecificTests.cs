// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class R4VersionSpecificTests
{
    private readonly DeIdEngineFixture _fixture;

    public R4VersionSpecificTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }
    public static IEnumerable<object[]> GetR4OnlyResources()
    {
        yield return ["R4OnlyResource/OrganizationAffiliation.json", "R4OnlyResource/OrganizationAffiliation-target.json"];
        yield return ["R4OnlyResource/MedicinalProduct.json", "R4OnlyResource/MedicinalProduct-target.json"];
        yield return ["R4OnlyResource/ServiceRequest.json", "R4OnlyResource/ServiceRequest-target.json"];
    }

    public static IEnumerable<object[]> GetCommonResourcesWithR4OnlyField()
    {
        yield return ["R4OnlyResource/Claim-R4.json", "R4OnlyResource/Claim-R4-target.json"];
        yield return ["R4OnlyResource/Account-R4.json", "R4OnlyResource/Account-R4-target.json"];
        yield return ["R4OnlyResource/Contract-R4.json", "R4OnlyResource/Contract-R4-target.json"];
    }

    [Theory]
    [MemberData(nameof(GetR4OnlyResources))]
    public async Task GivenAR4OnlyResource_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.R4ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath(testFile),
            DeIdTestHelpers.ResourcePath(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithR4OnlyField))]
    public async Task GivenCommonResourceWithR4OnlyField_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.R4ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath(testFile),
            DeIdTestHelpers.ResourcePath(targetFile));
    }
}
