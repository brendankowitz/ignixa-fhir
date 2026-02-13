// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Tests.Fixtures;
using Ignixa.Anonymizer.Tests.Utilities;

namespace Ignixa.Anonymizer.Tests;

[Collection("Anonymizer Engine Collection")]
public class Stu3VersionSpecificTests
{
    private readonly AnonymizerEngineFixture _fixture;

    public Stu3VersionSpecificTests(AnonymizerEngineFixture fixture)
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
    public async Task GivenAStu3OnlyResource_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.Stu3ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath(testFile),
            AnonymizerTestHelpers.ResourcePath(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyField))]
    public async Task GivenCommonResourceWithStu3OnlyField_WhenAnonymizing_ThenAnonymizedJsonMatchesExpected(string testFile, string targetFile)
    {
        var engine = _fixture.Stu3ConfigurationSampleEngine;
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(
            engine,
            AnonymizerTestHelpers.ResourcePath(testFile),
            AnonymizerTestHelpers.ResourcePath(targetFile));
    }
}
