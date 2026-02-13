// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.Anonymizer.Tests.Fixtures;

public sealed class AnonymizerEngineFixture : IDisposable
{
    private readonly ServiceProvider _commonServiceProvider;
    private readonly ServiceProvider _redactAllServiceProvider;
    private readonly ServiceProvider _r4SampleServiceProvider;
    private readonly ServiceProvider _stu3SampleServiceProvider;

    public IAnonymizerEngine R4CommonEngine { get; }
    public IAnonymizerEngine R4RedactAllEngine { get; }
    public IAnonymizerEngine R4ConfigurationSampleEngine { get; }
    public IAnonymizerEngine Stu3ConfigurationSampleEngine { get; }

    public AnonymizerEngineFixture()
    {
        var services = new ServiceCollection();

        // Register common dependencies
        var r4Schema = new R4CoreSchemaProvider();
        var stu3Schema = new STU3CoreSchemaProvider();

        services.AddSingleton<IFhirSchemaProvider>(r4Schema);
        services.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        services.AddLogging();

        // Register multiple engines with different configs
        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(Path.Combine("Configurations", "common-config.json"));
        });

        _commonServiceProvider = services.BuildServiceProvider();
        R4CommonEngine = _commonServiceProvider.GetRequiredService<IAnonymizerEngine>();

        // Create R4 RedactAll engine
        var redactAllServices = new ServiceCollection();
        redactAllServices.AddSingleton<IFhirSchemaProvider>(r4Schema);
        redactAllServices.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        redactAllServices.AddLogging();
        redactAllServices.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(Path.Combine("Configurations", "redact-all-config.json"));
        });
        _redactAllServiceProvider = redactAllServices.BuildServiceProvider();
        R4RedactAllEngine = _redactAllServiceProvider.GetRequiredService<IAnonymizerEngine>();

        // Create R4 configuration sample engine
        var r4SampleServices = new ServiceCollection();
        r4SampleServices.AddSingleton<IFhirSchemaProvider>(r4Schema);
        r4SampleServices.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        r4SampleServices.AddLogging();
        r4SampleServices.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile("r4-configuration-sample.json");
        });
        _r4SampleServiceProvider = r4SampleServices.BuildServiceProvider();
        R4ConfigurationSampleEngine = _r4SampleServiceProvider.GetRequiredService<IAnonymizerEngine>();

        // Create STU3 configuration sample engine
        var stu3SampleServices = new ServiceCollection();
        stu3SampleServices.AddSingleton<IFhirSchemaProvider>(stu3Schema);
        stu3SampleServices.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        stu3SampleServices.AddLogging();
        stu3SampleServices.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile("stu3-configuration-sample.json");
        });
        _stu3SampleServiceProvider = stu3SampleServices.BuildServiceProvider();
        Stu3ConfigurationSampleEngine = _stu3SampleServiceProvider.GetRequiredService<IAnonymizerEngine>();
    }

    public void Dispose()
    {
        _commonServiceProvider?.Dispose();
        _redactAllServiceProvider?.Dispose();
        _r4SampleServiceProvider?.Dispose();
        _stu3SampleServiceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
