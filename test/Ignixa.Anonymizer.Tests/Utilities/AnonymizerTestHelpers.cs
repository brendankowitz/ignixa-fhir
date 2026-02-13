// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.Anonymizer.Tests.Utilities;

public static class AnonymizerTestHelpers
{
    /// <summary>
    /// Creates a custom engine for tests that need non-standard configurations.
    /// Most tests should use the shared engines from AnonymizerEngineFixture instead.
    /// </summary>
    public static IAnonymizerEngine CreateEngine(
        string configPath,
        IFhirSchemaProvider? schema = null,
        Action<AnonymizerBuilder>? configure = null)
    {
        schema ??= new R4CoreSchemaProvider();

        var services = new ServiceCollection();
        services.AddSingleton(schema);
        services.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        services.AddLogging();
        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(configPath);
            configure?.Invoke(builder);
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAnonymizerEngine>();
    }

    public static IAnonymizerEngine CreateR4Engine(string configPath, Action<AnonymizerBuilder>? configure = null)
        => CreateEngine(configPath, new R4CoreSchemaProvider(), configure);

    public static IAnonymizerEngine CreateStu3Engine(string configPath)
        => CreateEngine(configPath, new STU3CoreSchemaProvider());

    public static async Task<Result<AnonymizationResult>> AnonymizeFromFileAsync(
        IAnonymizerEngine engine,
        string testFile,
        RequestOptions? settings = null)
    {
        string testContent = await File.ReadAllTextAsync(testFile);
        return await engine.AnonymizeAsync(testContent, settings);
    }

    public static string ConfigPath(string fileName)
        => Path.Combine("Configurations", fileName);

    public static string ResourcePath(string fileName)
        => Path.Combine("TestResources", fileName);
}
