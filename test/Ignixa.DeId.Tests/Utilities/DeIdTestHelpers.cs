// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.DeId.Tests.Utilities;

public static class DeIdTestHelpers
{
    /// <summary>
    /// Creates a custom engine for tests that need non-standard configurations.
    /// Most tests should use the shared engines from DeIdEngineFixture instead.
    /// </summary>
    public static IDeIdEngine CreateEngine(
        string configPath,
        IFhirSchemaProvider? schema = null,
        Action<DeIdBuilder>? configure = null)
    {
        schema ??= new R4CoreSchemaProvider();

        var services = new ServiceCollection();
        services.AddSingleton(schema);
        services.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        services.AddLogging();
        services.AddFhirDeId(builder =>
        {
            builder.WithConfigurationFile(configPath);
            configure?.Invoke(builder);
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDeIdEngine>();
    }

    public static IDeIdEngine CreateR4Engine(string configPath, Action<DeIdBuilder>? configure = null)
        => CreateEngine(configPath, new R4CoreSchemaProvider(), configure);

    public static IDeIdEngine CreateStu3Engine(string configPath)
        => CreateEngine(configPath, new STU3CoreSchemaProvider());

    public static async Task<Result<DeIdResult>> DeidentifyFromFileAsync(
        IDeIdEngine engine,
        string testFile,
        RequestOptions? settings = null)
    {
        string testContent = await File.ReadAllTextAsync(testFile);
        return await engine.DeidentifyAsync(testContent, settings);
    }

    public static string ConfigPath(string fileName)
        => Path.Combine("Configurations", fileName);

    public static string ResourcePath(string fileName)
        => Path.Combine("TestResources", fileName);
}
