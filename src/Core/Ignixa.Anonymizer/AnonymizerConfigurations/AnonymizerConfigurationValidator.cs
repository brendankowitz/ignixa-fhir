// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Security.Cryptography;
using System.Text;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Processors.Settings;

namespace Ignixa.Anonymizer.AnonymizerConfigurations;

public class AnonymizerConfigurationValidator
{
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<AnonymizerConfigurationValidator>();

    public void Validate(AnonymizerConfiguration config)
    {
        if (string.IsNullOrEmpty(config.FhirVersion))
        {
            _logger.LogWarning("Version is not specified in configuration file.");
        }
        else if (!Constants.SupportedFhirVersions.Contains(config.FhirVersion))
        {
            throw new AnonymizerConfigurationException(
                $"Configuration of fhirVersion {config.FhirVersion} is not supported. " +
                $"Supported versions: {string.Join(", ", Constants.SupportedFhirVersions)}");
        }

        if (config.FhirPathRules is null || config.FhirPathRules.Length == 0)
        {
            throw new AnonymizerConfigurationException("The configuration is invalid, please specify any fhirPathRules");
        }

        var parser = new FhirPathParser();
        var supportedMethods = Enum.GetNames(typeof(AnonymizerMethod)).ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        foreach (var rule in config.FhirPathRules)
        {
            if (!rule.ContainsKey(Constants.PathKey) || !rule.ContainsKey(Constants.MethodKey))
            {
                throw new AnonymizerConfigurationException("Missing path or method in Fhir path rule config.");
            }

            // Grammar check on FHIR path
            try
            {
                parser.Parse(rule[Constants.PathKey].ToString()!);
            }
            catch (Exception ex)
            {
                throw new AnonymizerConfigurationException($"Invalid FHIR path {rule[Constants.PathKey]}", ex);
            }

            // Method validate
            string method = rule[Constants.MethodKey].ToString()!;
            if (!supportedMethods.Contains(method))
            {
                _logger.LogWarning("Anonymization method {Method} is not a built-in method. Please make sure method {Method} has been added as custom processor.", method, method);
            }

            if (string.Equals(method, AnonymizerMethod.Substitute.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                SubstituteSetting.ValidateRuleSettings(rule);
            }

            if (string.Equals(method, AnonymizerMethod.Perturb.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                PerturbSetting.ValidateRuleSettings(rule);
            }

            if (string.Equals(method, AnonymizerMethod.Generalize.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                GeneralizeSetting.ValidateRuleSettings(rule);
            }
        }

        // Check AES key size is valid (16, 24 or 32 bytes).
        if (!string.IsNullOrEmpty(config.ParameterConfiguration?.EncryptKey))
        {
            using var aes = Aes.Create();
            var encryptKeySize = Encoding.UTF8.GetByteCount(config.ParameterConfiguration.EncryptKey) * 8;
            if (!IsValidKeySize(encryptKeySize, aes.LegalKeySizes))
            {
                throw new AnonymizerConfigurationException($"Invalid encrypt key size : {encryptKeySize} bits! Please provide key sizes of 128, 192 or 256 bits.");
            }
        }
    }

    private static bool IsValidKeySize(int bitLength, KeySizes[] validSizes)
    {
        if (validSizes is null)
        {
            return false;
        }

        foreach (var size in validSizes)
        {
            for (int j = size.MinSize; j <= size.MaxSize; j += size.SkipSize)
            {
                if (j == bitLength)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
