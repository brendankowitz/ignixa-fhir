// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Pipeline;
using Ignixa.Anonymizer.Processors;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ignixa.Anonymizer.Extensions;

/// <summary>
/// Dependency injection extensions for registering FHIR anonymizer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers FHIR anonymizer services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration builder action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFhirAnonymizer(
        this IServiceCollection services,
        Action<AnonymizerBuilder>? configure = null)
    {
        var builder = new AnonymizerBuilder(services);
        configure?.Invoke(builder);

        // Register core services
        services.TryAddSingleton<IAnonymizerEngine, AnonymizerEngine>();

        // Register validation schema resolver
        services.TryAddSingleton<IValidationSchemaResolver, CachedValidationSchemaResolver>();

        // Register pipeline and handlers
        services.TryAddSingleton<IAnonymizerPipeline, AnonymizerPipeline>();
        services.TryAddSingleton<ValidationHandler>();
        services.TryAddSingleton<RuleMatchingHandler>();
        services.TryAddSingleton<ProcessorHandler>();
        services.TryAddSingleton<SecurityTagHandler>();
        services.TryAddSingleton<OutputFormattingHandler>();

        // Register default handler array (can be overridden via builder)
        services.TryAddSingleton<AnonymizerPipelineHandler[]>(sp =>
        [
            sp.GetRequiredService<ValidationHandler>(),
            sp.GetRequiredService<RuleMatchingHandler>(),
            sp.GetRequiredService<ProcessorHandler>(),
            sp.GetRequiredService<SecurityTagHandler>(),
            sp.GetRequiredService<OutputFormattingHandler>()
        ]);

        // Register built-in processors as keyed services with factory methods
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("REDACT", CreateRedactProcessor);
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("DATESHIFT", CreateDateShiftProcessor);
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("CRYPTOHASH", CreateCryptoHashProcessor);
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("ENCRYPT", CreateEncryptProcessor);
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("SUBSTITUTE", (_, _) => new SubstituteProcessor());
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("PERTURB", CreatePerturbProcessor);
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("KEEP", (_, _) => new KeepProcessor());
        services.TryAddKeyedSingleton<IAnonymizerProcessor>("GENERALIZE", (_, _) => new GeneralizeProcessor());

        return services;
    }

    private static RedactProcessor CreateRedactProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<AnonymizerOptions>>().Value;
        var parameters = options.Parameters;
        var restrictedZips = parameters?.RestrictedZipCodeTabulationAreas?.ToList();
        return new RedactProcessor(
            parameters?.EnablePartialDatesForRedact ?? false,
            parameters?.EnablePartialAgesForRedact ?? false,
            parameters?.EnablePartialZipCodesForRedact ?? false,
            restrictedZips ?? []);
    }

    private static DateShiftProcessor CreateDateShiftProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<AnonymizerOptions>>().Value;
        var parameters = options.Parameters;
        var dateShiftKey = parameters?.DateShiftKey;
        if (string.IsNullOrWhiteSpace(dateShiftKey))
        {
            dateShiftKey = Guid.NewGuid().ToString("N");
        }
        return new DateShiftProcessor(
            dateShiftKey,
            string.Empty,
            parameters?.EnablePartialDatesForRedact ?? true,
            parameters?.DateShiftFixedOffsetInDays);
    }

    private static CryptoHashProcessor CreateCryptoHashProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<AnonymizerOptions>>().Value;
        var schema = sp.GetRequiredService<IFhirSchemaProvider>();
        var cryptoHashKey = options.Parameters?.CryptoHashKey;
        if (string.IsNullOrWhiteSpace(cryptoHashKey))
        {
            cryptoHashKey = Guid.NewGuid().ToString("N");
        }
        return new CryptoHashProcessor(cryptoHashKey, schema);
    }

    private static EncryptProcessor CreateEncryptProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<AnonymizerOptions>>().Value;
        var encryptKey = options.Parameters?.EncryptKey;
        if (string.IsNullOrWhiteSpace(encryptKey))
        {
            encryptKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
        return new EncryptProcessor(encryptKey);
    }

    private static PerturbProcessor CreatePerturbProcessor(IServiceProvider sp, object? key)
    {
        var schema = sp.GetRequiredService<IFhirSchemaProvider>();
        return new PerturbProcessor(schema);
    }
}

/// <summary>
/// Builder for configuring FHIR anonymizer services.
/// </summary>
public sealed class AnonymizerBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers a custom anonymization processor.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type.</typeparam>
    /// <param name="method">The method name (case-insensitive).</param>
    /// <returns>The builder for chaining.</returns>
    public AnonymizerBuilder AddProcessor<TProcessor>(string method)
        where TProcessor : class, IAnonymizerProcessor
    {
        Services.AddKeyedSingleton<IAnonymizerProcessor, TProcessor>(method.ToUpperInvariant());
        return this;
    }

    /// <summary>
    /// Configures anonymizer options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    /// <returns>The builder for chaining.</returns>
    public AnonymizerBuilder WithOptions(Action<OptionsBuilder<AnonymizerOptions>> configure)
    {
        var optionsBuilder = Services.AddOptions<AnonymizerOptions>();
        configure(optionsBuilder);
        return this;
    }

    /// <summary>
    /// Loads anonymizer options from a JSON configuration file.
    /// </summary>
    /// <param name="configFilePath">Path to the configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    public AnonymizerBuilder WithConfigurationFile(string configFilePath)
    {
        Services.Configure<AnonymizerOptions>(options =>
        {
            var result = AnonymizerOptionsLoader.LoadFromFile(configFilePath);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to load anonymizer configuration: {result.Error.Message}",
                    result.Error.Exception);
            }

            var loaded = result.Value;
            // Copy properties from loaded config to options
            // This is a workaround since we can't directly replace the options instance
            typeof(AnonymizerOptions).GetProperty(nameof(AnonymizerOptions.FhirVersion))!
                .SetValue(options, loaded.FhirVersion);
            typeof(AnonymizerOptions).GetProperty(nameof(AnonymizerOptions.Rules))!
                .SetValue(options, loaded.Rules);
            typeof(AnonymizerOptions).GetProperty(nameof(AnonymizerOptions.Parameters))!
                .SetValue(options, loaded.Parameters);
            typeof(AnonymizerOptions).GetProperty(nameof(AnonymizerOptions.Processing))!
                .SetValue(options, loaded.Processing);
        });
        return this;
    }
}
