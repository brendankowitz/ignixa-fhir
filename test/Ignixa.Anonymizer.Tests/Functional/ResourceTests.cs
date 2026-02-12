// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests;

public class ResourceTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-basic.json"), ResourceTestsFile("patient-basic-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenRedactAll_ThenRedactedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-basic.json"), ResourceTestsFile("patient-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResourceAsync_WhenAnonymizing_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        string testContent = await File.ReadAllTextAsync(ResourceTestsFile("patient-basic.json"));

        var result = await engine.AnonymizeAsync(testContent, _schema);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
        result.Value.AnonymizedJson.ShouldContain("\"resourceType\":\"Patient\"");
    }

    [Fact]
    public async Task GivenAPatientResourceAsync_WhenRedactAll_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        string testContent = await File.ReadAllTextAsync(ResourceTestsFile("patient-basic.json"));

        var result = await engine.AnonymizeAsync(testContent, _schema);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAPatientResourceAsync_WhenAnonymizing_ThenMetricsAreReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));

        var result = await FunctionalTestUtility.AnonymizeFromFileAsync(
            engine,
            _schema,
            ResourceTestsFile("patient-basic.json"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Metrics.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAPatientResourceAsync_WhenAnonymizingWithGeneralizeConfig_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "generalize-patient-config.json"));
        string testContent = await File.ReadAllTextAsync(ResourceTestsFile("patient-generalize.json"));

        var result = await engine.AnonymizeAsync(testContent, _schema);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAPatientResourceAsync_WhenAnonymizingWithSubstituteConfig_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-primitive.json"));
        string testContent = await File.ReadAllTextAsync(ResourceTestsFile("patient-substitute-primitive.json"));

        var result = await engine.AnonymizeAsync(testContent, _schema);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAPatientResourceWithSpecialContents_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-special-content.json"), ResourceTestsFile("patient-special-content-target.json"));
    }

    [Fact(Skip = "Ignixa SDK bug: _birthDate without birthDate produces empty InstanceType on all children - https://github.com/brendankowitz/ignixa-fhir/issues/216")]
    public async Task GivenAPatientResourceWithNullDatetime_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-null-date.json"), ResourceTestsFile("patient-null-date-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithNoPartialRedactConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-no-partial-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-no-partial.json"), ResourceTestsFile("patient-no-partial-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithPrimitiveSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-primitive.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-substitute-primitive.json"), ResourceTestsFile("patient-substitute-primitive-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithComplexSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-complex.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-substitute-complex.json"), ResourceTestsFile("patient-substitute-complex-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithConflictRulesSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-conflict-rules.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-substitute-conflict-rules.json"), ResourceTestsFile("patient-substitute-conflict-rules-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithGeneralizeConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "generalize-patient-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-generalize.json"), ResourceTestsFile("patient-generalize-target.json"));
    }

    [Fact]
    public async Task GivenAPatientResource_WhenAnonymizingWithMultipleSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-substitute-multiple.json"), ResourceTestsFile("patient-substitute-multiple-target.json"));

        engine = CreateEngine(Path.Combine("Configurations", "substitute-multiple-2.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, _schema, ResourceTestsFile("patient-substitute-multiple-2.json"), ResourceTestsFile("patient-substitute-multiple-2-target.json"));
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

    private static string ResourceTestsFile(string fileName)
    {
        return Path.Combine("TestResources", fileName);
    }
}
