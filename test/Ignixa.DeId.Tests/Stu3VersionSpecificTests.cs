// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Tests.Fixtures;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests;

[Collection("DeId Engine Collection")]
public class Stu3VersionSpecificTests
{
    private readonly DeIdEngineFixture _fixture;

    public Stu3VersionSpecificTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }
    public static IEnumerable<object[]> GetStu3OnlyResources()
    {
        yield return ["Stu3OnlyResource/DeviceComponent.json", "Stu3OnlyResource/DeviceComponent-target.json"];
        yield return ["Stu3OnlyResource/ProcessRequest.json", "Stu3OnlyResource/ProcessRequest-target.json"];
        yield return ["Stu3OnlyResource/ProcessResponse.json", "Stu3OnlyResource/ProcessResponse-target.json"];
    }

    public static IEnumerable<object[]> GetCommonResourcesWithStu3OnlyField()
    {
        yield return ["Stu3OnlyResource/Claim-Stu3.json", "Stu3OnlyResource/Claim-Stu3-target.json"];
        yield return ["Stu3OnlyResource/Account-Stu3.json", "Stu3OnlyResource/Account-Stu3-target.json"];
        yield return ["Stu3OnlyResource/Contract-Stu3.json", "Stu3OnlyResource/Contract-Stu3-target.json"];
    }

    [Theory]
    [MemberData(nameof(GetStu3OnlyResources))]
    public async Task GivenAStu3OnlyResource_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.Stu3ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath(testFile),
            DeIdTestHelpers.ResourcePath(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyField))]
    public async Task GivenCommonResourceWithStu3OnlyField_WhenAnonymizing_ThenDeidentifiedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.Stu3ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            DeIdTestHelpers.ResourcePath(testFile),
            DeIdTestHelpers.ResourcePath(targetFile));
    }
}
