// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Pipeline;
using Ignixa.DeId.Processors;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.DeId.Extensions;

/// <summary>
/// Dependency injection extensions for registering FHIR de-identifier services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers FHIR de-identifier services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration builder action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFhirDeId(
        this IServiceCollection services,
        Action<DeIdBuilder>? configure = null)
    {
        var builder = new DeIdBuilder(services);
        configure?.Invoke(builder);

        // Register core services
        services.TryAddSingleton<IDeIdEngine, DeIdEngine>();

        // Register validation schema resolver
        services.TryAddSingleton<IValidationSchemaResolver, CachedValidationSchemaResolver>();

        // Register pipeline and handlers
        services.TryAddSingleton<IDeIdPipeline, DeIdPipeline>();
        services.TryAddSingleton<ValidationHandler>();
        services.TryAddSingleton<RuleMatchingHandler>();
        services.TryAddSingleton<ProcessorHandler>();
        services.TryAddSingleton<SecurityTagHandler>();
        services.TryAddSingleton<OutputFormattingHandler>();

        // Register default handler array (can be overridden via builder)
        services.TryAddSingleton<DeIdPipelineHandler[]>(sp =>
        [
            sp.GetRequiredService<ValidationHandler>(),
            sp.GetRequiredService<RuleMatchingHandler>(),
            sp.GetRequiredService<ProcessorHandler>(),
            sp.GetRequiredService<SecurityTagHandler>(),
            sp.GetRequiredService<OutputFormattingHandler>()
        ]);

        // Register built-in processors as keyed services with factory methods
        services.TryAddKeyedSingleton<IDeIdProcessor>("REDACT", CreateRedactProcessor);
        services.TryAddKeyedSingleton<IDeIdProcessor>("DATESHIFT", CreateDateShiftProcessor);
        services.TryAddKeyedSingleton<IDeIdProcessor>("CRYPTOHASH", CreateCryptoHashProcessor);
        services.TryAddKeyedSingleton<IDeIdProcessor>("ENCRYPT", CreateEncryptProcessor);
        services.TryAddKeyedSingleton<IDeIdProcessor>("SUBSTITUTE", (_, _) => new SubstituteProcessor());
        services.TryAddKeyedSingleton<IDeIdProcessor>("PERTURB", CreatePerturbProcessor);
        services.TryAddKeyedSingleton<IDeIdProcessor>("KEEP", (_, _) => new KeepProcessor());
        services.TryAddKeyedSingleton<IDeIdProcessor>("GENERALIZE", (_, _) => new GeneralizeProcessor());

        return services;
    }

    private static RedactProcessor CreateRedactProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<DeIdOptions>>().Value;
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
        var options = sp.GetRequiredService<IOptions<DeIdOptions>>().Value;
        var parameters = options.Parameters;
        var dateShiftKey = parameters?.DateShiftKey;
        if (string.IsNullOrWhiteSpace(dateShiftKey))
        {
            var logger = sp.GetService<ILogger<DateShiftProcessor>>();
            logger?.LogWarning(
                "No DateShiftKey configured. Using an auto-generated key. " +
                "De-identification will not be deterministic across runs.");
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
        var options = sp.GetRequiredService<IOptions<DeIdOptions>>().Value;
        var schema = sp.GetRequiredService<IFhirSchemaProvider>();
        var cryptoHashKey = options.Parameters?.CryptoHashKey;
        if (string.IsNullOrWhiteSpace(cryptoHashKey))
        {
            var logger = sp.GetService<ILogger<CryptoHashProcessor>>();
            logger?.LogWarning(
                "No CryptoHashKey configured. Using an auto-generated key. " +
                "De-identification will not be deterministic across runs.");
            cryptoHashKey = Guid.NewGuid().ToString("N");
        }
        return new CryptoHashProcessor(cryptoHashKey, schema);
    }

    private static EncryptProcessor CreateEncryptProcessor(IServiceProvider sp, object? key)
    {
        var options = sp.GetRequiredService<IOptions<DeIdOptions>>().Value;
        var encryptKey = options.Parameters?.EncryptKey;
        if (string.IsNullOrWhiteSpace(encryptKey))
        {
            var logger = sp.GetService<ILogger<EncryptProcessor>>();
            logger?.LogWarning(
                "No EncryptKey configured. Using an auto-generated key. " +
                "De-identification will not be deterministic across runs.");
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
/// Builder for configuring FHIR de-identifier services.
/// </summary>
public sealed class DeIdBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers a custom de-identification processor.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type.</typeparam>
    /// <param name="method">The method name (case-insensitive).</param>
    /// <returns>The builder for chaining.</returns>
    public DeIdBuilder AddProcessor<TProcessor>(string method)
        where TProcessor : class, IDeIdProcessor
    {
        Services.AddKeyedSingleton<IDeIdProcessor, TProcessor>(method.ToUpperInvariant());
        return this;
    }

    /// <summary>
    /// Configures de-identifier options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    /// <returns>The builder for chaining.</returns>
    public DeIdBuilder WithOptions(Action<OptionsBuilder<DeIdOptions>> configure)
    {
        var optionsBuilder = Services.AddOptions<DeIdOptions>();
        configure(optionsBuilder);
        return this;
    }

    /// <summary>
    /// Loads de-identifier options from a JSON configuration file.
    /// </summary>
    /// <param name="configFilePath">Path to the configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    public DeIdBuilder WithConfigurationFile(string configFilePath)
    {
        Services.Configure<DeIdOptions>(options =>
        {
            var result = DeIdOptionsLoader.LoadFromFile(configFilePath);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to load de-identifier configuration: {result.Error.Message}",
                    result.Error.Exception);
            }

            var loaded = result.Value;
            // Copy properties from loaded config to options
            // This is a workaround since we can't directly replace the options instance
            typeof(DeIdOptions).GetProperty(nameof(DeIdOptions.FhirVersion))!
                .SetValue(options, loaded.FhirVersion);
            typeof(DeIdOptions).GetProperty(nameof(DeIdOptions.Rules))!
                .SetValue(options, loaded.Rules);
            typeof(DeIdOptions).GetProperty(nameof(DeIdOptions.Parameters))!
                .SetValue(options, loaded.Parameters);
            typeof(DeIdOptions).GetProperty(nameof(DeIdOptions.Processing))!
                .SetValue(options, loaded.Processing);
        });
        return this;
    }
}
