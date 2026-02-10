// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Ignixa.Anonymizer;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests;

public class R4VersionSpecificTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetStu3OnlyResources()
    {
        yield return new object[] { "Stu3OnlyResource/DeviceComponent.json", "DeviceComponent" };
        yield return new object[] { "Stu3OnlyResource/ProcessRequest.json", "ProcessRequest" };
        yield return new object[] { "Stu3OnlyResource/ProcessResponse.json", "ProcessResponse" };
    }

    public static IEnumerable<object[]> GetR4OnlyResources()
    {
        yield return new object[] { "R4OnlyResource/OrganizationAffiliation.json", "R4OnlyResource/OrganizationAffiliation-target.json" };
        yield return new object[] { "R4OnlyResource/MedicinalProduct.json", "R4OnlyResource/MedicinalProduct-target.json" };
        yield return new object[] { "R4OnlyResource/ServiceRequest.json", "R4OnlyResource/ServiceRequest-target.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithStu3OnlyValue()
    {
        yield return new object[] { "Stu3OnlyResource/Claim-Stu3.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithStu3OnlyElement()
    {
        yield return new object[] { "Stu3OnlyResource/Account-Stu3.json" };
        yield return new object[] { "Stu3OnlyResource/Contract-Stu3.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithR4OnlyField()
    {
        yield return new object[] { "R4OnlyResource/Claim-R4.json", "R4OnlyResource/Claim-R4-target.json" };
        yield return new object[] { "R4OnlyResource/Account-R4.json", "R4OnlyResource/Account-R4-target.json" };
        yield return new object[] { "R4OnlyResource/Contract-R4.json", "R4OnlyResource/Contract-R4-target.json" };
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown resource types are accepted and ignored.")]
    [MemberData(nameof(GetStu3OnlyResources))]
    public void GivenAStu3OnlyResource_WhenAnonymizingWithR4_ExceptionShouldBeThrown(string testFile, string resourceName)
    {
        AnonymizerEngine engine = new AnonymizerEngine("r4-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
    }

    [Theory]
    [MemberData(nameof(GetR4OnlyResources))]
    public void GivenAR4OnlyResource_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("r4-configuration-sample.json", _schema);
        FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithR4OnlyField))]
    public void GivenCommonResourceWithR4OnlyField_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("r4-configuration-sample.json", _schema);
        FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown field values are accepted and ignored.")]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyValue))]
    public void GivenCommonResourceWithStu3OnlyValue_WhenAnonymizingWithR4_ExceptionShouldBeThrown(string testFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("r4-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown elements are accepted and ignored.")]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyElement))]
    public void GivenCommonResourceWithStu3OnlyElement_WhenAnonymizingWithR4_ExceptionShouldBeThrown(string testFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("r4-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
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

    public static IEnumerable<object[]> GetR4OnlyResources()
    {
        yield return new object[] { "R4OnlyResource/OrganizationAffiliation.json", "OrganizationAffiliation" };
        yield return new object[] { "R4OnlyResource/MedicinalProduct.json", "MedicinalProduct" };
        yield return new object[] { "R4OnlyResource/ServiceRequest.json", "ServiceRequest" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithStu3OnlyField()
    {
        yield return new object[] { "Stu3OnlyResource/Claim-Stu3.json", "Stu3OnlyResource/Claim-Stu3-target.json" };
        yield return new object[] { "Stu3OnlyResource/Account-Stu3.json", "Stu3OnlyResource/Account-Stu3-target.json" };
        yield return new object[] { "Stu3OnlyResource/Contract-Stu3.json", "Stu3OnlyResource/Contract-Stu3-target.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithR4OnlyValue()
    {
        yield return new object[] { "R4OnlyResource/Claim-R4.json" };
    }

    public static IEnumerable<object[]> GetCommonResourcesWithR4OnlyElement()
    {
        yield return new object[] { "R4OnlyResource/Contract-R4.json" };
        yield return new object[] { "R4OnlyResource/Account-R4.json" };
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown resource types are accepted and ignored.")]
    [MemberData(nameof(GetR4OnlyResources))]
    public void GivenAR4OnlyResource_WhenAnonymizingWithStu3_ExceptionShouldBeThrown(string testFile, string resourceName)
    {
        AnonymizerEngine engine = new AnonymizerEngine("stu3-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
    }

    [Theory]
    [MemberData(nameof(GetStu3OnlyResources))]
    public void GivenAStu3OnlyResource_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("stu3-configuration-sample.json", _schema);
        FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory]
    [MemberData(nameof(GetCommonResourcesWithStu3OnlyField))]
    public void GivenCommonResourceWithStu3OnlyField_WhenAnonymizing_AnonymizedJsonShouldBeReturned(string testFile, string targetFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("stu3-configuration-sample.json", _schema);
        FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile(testFile), ResourceTestsFile(targetFile));
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown field values are accepted and ignored.")]
    [MemberData(nameof(GetCommonResourcesWithR4OnlyValue))]
    public void GivenCommonResourceWithR4OnlyValue_WhenAnonymizingWithStu3_ExceptionShouldBeThrown(string testFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("stu3-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
    }

    [Theory(Skip = "Ignixa SDK: Parser is lenient by design. Validation is a separate library, not enforced during parsing. Unknown elements are accepted and ignored.")]
    [MemberData(nameof(GetCommonResourcesWithR4OnlyElement))]
    public void GivenCommonResourceWithR4OnlyElement_WhenAnonymizingWithStu3_ExceptionShouldBeThrown(string testFile)
    {
        AnonymizerEngine engine = new AnonymizerEngine("stu3-configuration-sample.json", _schema);
        string testContent = File.ReadAllText(ResourceTestsFile(testFile));
        Assert.ThrowsAny<Exception>(() => engine.AnonymizeJson(testContent));
    }

    private static string ResourceTestsFile(string fileName) => Path.Combine("TestResources", fileName);
}
