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
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests;

public class CollectionResourceTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public async Task GivenAResourceWithContained_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("contained-basic.json"), CollectionResourceTestsFile("contained-basic-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContainedAsync_WhenAnonymizing_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        string testContent = await File.ReadAllTextAsync(CollectionResourceTestsFile("contained-basic.json"));

        var result = await engine.AnonymizeAsync(testContent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenABundleResourceAsync_WhenAnonymizing_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        string testContent = await File.ReadAllTextAsync(CollectionResourceTestsFile("bundle-basic.json"));

        var result = await engine.AnonymizeAsync(testContent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
        result.Value.AnonymizedJson.ShouldContain("\"resourceType\":\"Bundle\"");
    }

    [Fact]
    public async Task GivenABundleResourceWithContainedAsync_WhenAnonymizing_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        string testContent = await File.ReadAllTextAsync(CollectionResourceTestsFile("contained-in-bundle.json"));

        var result = await engine.AnonymizeAsync(testContent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAResourceWithContainedAsync_WhenRedactAll_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        string testContent = await File.ReadAllTextAsync(CollectionResourceTestsFile("contained-basic.json"));

        var result = await engine.AnonymizeAsync(testContent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenABundleResourceAsync_WhenRedactAll_ThenReturnsSuccessResult()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        string testContent = await File.ReadAllTextAsync(CollectionResourceTestsFile("bundle-basic.json"));

        var result = await engine.AnonymizeAsync(testContent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AnonymizedJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenABundleResource_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("bundle-basic.json"), CollectionResourceTestsFile("bundle-basic-target.json"));
    }

    [Fact]
    public async Task GivenABundleResourceWithContainedInside_WhenAnonymizing_ThenContainedResourceShouldBeAnonymized()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "common-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("contained-in-bundle.json"), CollectionResourceTestsFile("contained-in-bundle-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenRedactAll_ThenRedactedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("contained-basic.json"), CollectionResourceTestsFile("contained-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenRedactAll_ThenRedactedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("bundle-basic.json"), CollectionResourceTestsFile("bundle-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenABundleResourceWithContainedInside_WhenRedactAll_ThenRedactedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "redact-all-config.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("contained-in-bundle.json"), CollectionResourceTestsFile("contained-in-bundle-redact-all-target.json"));
    }

    [Fact]
    public async Task GivenAResourceWithContained_WhenSubstitute_ThenSubstitutedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("contained-substitute.json"), CollectionResourceTestsFile("contained-substitute-target.json"));
    }

    [Fact]
    public async Task GivenABundleResource_WhenSubstitute_ThenAnonymizedJsonShouldBeReturned()
    {
        var engine = CreateEngine(Path.Combine("Configurations", "substitute-multiple.json"));
        await FunctionalTestUtility.VerifySingleJsonResourceFromFileAsync(engine, CollectionResourceTestsFile("bundle-substitute.json"), CollectionResourceTestsFile("bundle-substitute-target.json"));
    }

    private IAnonymizerEngine CreateEngine(string configPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFhirSchemaProvider>(_schema);
        services.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        services.AddLogging();
        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(configPath);
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAnonymizerEngine>();
    }

    private static string CollectionResourceTestsFile(string fileName)
    {
        return Path.Combine("TestResources", fileName);
    }
}
