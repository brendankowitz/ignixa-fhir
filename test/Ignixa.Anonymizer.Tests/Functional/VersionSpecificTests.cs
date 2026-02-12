// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests;

public class R4VersionSpecificTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetR4OnlyResources()
    {
        yield return new object[] { "R4OnlyResource/OrganizationAffiliation.json", "R4OnlyResource/OrganizationAffiliation-target.json" };
        yield return new object[] { "R4OnlyResource/MedicinalProduct.json", "R4OnlyResource/MedicinalProduct-target.json" };
        yield return new object[] { "R4OnlyResource/ServiceRequest.json", "R4OnlyResource/ServiceRequest-target.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithR4OnlyField()
    {
        yield return new object[] { "R4OnlyResource/Claim-R4.json", "R4OnlyResource/Claim-R4-target.json" };
        yield return new object[] { "R4OnlyResource/Account-R4.json", "R4OnlyResource/Account-R4-target.json" };
        yield return new object[] { "R4OnlyResource/Contract-R4.json", "R4OnlyResource/Contract-R4-target.json" };
    }

    [Theory]
    [MemberData(nameof(GetR4OnlyResources))]
    public async Task GivenAR4OnlyResource_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        var engine = CreateEngine("r4-configuration-sample.json");
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithR4OnlyField))]
    public async Task GivenCommonResourceWithR4OnlyField_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        var engine = CreateEngine("r4-configuration-sample.json");
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    private IAnonymizerEngine CreateEngine(string configPath)
    {
        var services = new ServiceCollection();
        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(configPath);
        });
        services.AddSingleton<IFhirSchemaProvider>(_schema);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAnonymizerEngine>();
    }

    private static string ResourceTestsFile(string fileName) => Path.Combine("TestResources", fileName);
}

public class Stu3VersionSpecificTests
{
    private readonly STU3CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetStu3OnlyResources()
    {
        yield return new object[] { "Stu3OnlyResource/DeviceComponent.json", "Stu3OnlyResource/DeviceComponent-target.json" };
        yield return new object[] { "Stu3OnlyResource/ProcessRequest.json", "Stu3OnlyResource/ProcessRequest-target.json" };
        yield return new object[] { "Stu3OnlyResource/ProcessResponse.json", "Stu3OnlyResource/ProcessResponse-target.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithStu3OnlyField()
    {
        yield return new object[] { "Stu3OnlyResource/Claim-Stu3.json", "Stu3OnlyResource/Claim-Stu3-target.json" };
        yield return new object[] { "Stu3OnlyResource/Account-Stu3.json", "Stu3OnlyResource/Account-Stu3-target.json" };
        yield return new object[] { "Stu3OnlyResource/Contract-Stu3.json", "Stu3OnlyResource/Contract-Stu3-target.json" };
    }

    [Theory]
    [MemberData(nameof(GetStu3OnlyResources))]
    public async Task GivenAStu3OnlyResource_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        var engine = CreateEngine("stu3-configuration-sample.json");
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyField))]
    public async Task GivenCommonResourceWithStu3OnlyField_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        var engine = CreateEngine("stu3-configuration-sample.json");
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    private IAnonymizerEngine CreateEngine(string configPath)
    {
        var services = new ServiceCollection();
        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(configPath);
        });
        services.AddSingleton<IFhirSchemaProvider>(_schema);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAnonymizerEngine>();
    }

    private static string ResourceTestsFile(string fileName) => Path.Combine("TestResources", fileName);
}
