// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json;
using EnsureThat;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;

namespace Ignixa.Anonymizer;

public sealed class AnonymizerConfigurationManager
{
    private readonly AnonymizerConfigurationValidator _validator = new();
    private readonly AnonymizerConfiguration _configuration;

    public AnonymizationFhirPathRule[] FhirPathRules { get; private set; } = null!;
    public AnonymizerConfiguration Configuration => _configuration;

    public AnonymizerConfigurationManager(AnonymizerConfiguration configuration)
    {
        EnsureArg.IsNotNull(configuration, nameof(configuration));

        _validator.Validate(configuration);
        configuration.GenerateDefaultParametersIfNotConfigured();

        _configuration = configuration;

        FhirPathRules = _configuration.FhirPathRules
            .Select(entry => AnonymizationFhirPathRule.CreateAnonymizationFhirPathRule(entry))
            .DistinctBy(r => r.Path)
            .ToArray();
    }

    public static AnonymizerConfigurationManager CreateFromSettingsInJson(string settingsInJson)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var configuration = JsonSerializer.Deserialize<AnonymizerConfiguration>(settingsInJson, options);
            if (configuration is null)
            {
                throw new AnonymizerConfigurationException("Configuration deserialized to null.");
            }

            return new AnonymizerConfigurationManager(configuration);
        }
        catch (JsonException innerException)
        {
            throw new AnonymizerConfigurationException("Failed to parse configuration file", innerException);
        }
    }

    public static AnonymizerConfigurationManager CreateFromConfigurationFile(string configFilePath)
    {
        try
        {
            var content = File.ReadAllText(configFilePath);
            return CreateFromSettingsInJson(content);
        }
        catch (IOException innerException)
        {
            throw new AnonymizerConfigurationException($"Failed to read configuration file {configFilePath}", innerException);
        }
    }

    public ParameterConfiguration GetParameterConfiguration()
    {
        return _configuration.ParameterConfiguration;
    }

    public void SetDateShiftKeyPrefix(string prefix)
    {
        _configuration.ParameterConfiguration.DateShiftKeyPrefix = prefix;
    }
}
