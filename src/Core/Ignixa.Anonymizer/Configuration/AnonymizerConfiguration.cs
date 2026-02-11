// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Serialization;

namespace Ignixa.Anonymizer.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<ProcessingErrorsOption>))]
public enum ProcessingErrorsOption
{
    Raise, // Invalid processing will raise an exception.
    Skip,  // Invalid processing will return empty element.
    // Ignore Invalid processing will return input.
}

public class AnonymizerConfiguration
{
    [JsonPropertyName("fhirVersion")]
    public string? FhirVersion { get; set; }

    [JsonPropertyName("processingErrors")]
    public ProcessingErrorsOption processingErrors { get; set; } = ProcessingErrorsOption.Raise;

    [JsonPropertyName("fhirPathRules")]
    public Dictionary<string, object>[] FhirPathRules { get; set; } = [];

    [JsonPropertyName("parameters")]
    public ParameterConfiguration ParameterConfiguration { get; set; } = new();

    private static readonly Lazy<string> DefaultCryptoKey = new(() => Guid.NewGuid().ToString("N"));

    public void GenerateDefaultParametersIfNotConfigured()
    {
        if (ParameterConfiguration is null)
        {
            ParameterConfiguration = new ParameterConfiguration
            {
                DateShiftKey = Guid.NewGuid().ToString("N"),
                CryptoHashKey = DefaultCryptoKey.Value,
                EncryptKey = DefaultCryptoKey.Value
            };
            return;
        }

        if (string.IsNullOrEmpty(ParameterConfiguration.DateShiftKey))
        {
            ParameterConfiguration.DateShiftKey = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrEmpty(ParameterConfiguration.CryptoHashKey))
        {
            ParameterConfiguration.CryptoHashKey = DefaultCryptoKey.Value;
        }

        if (string.IsNullOrEmpty(ParameterConfiguration.EncryptKey))
        {
            ParameterConfiguration.EncryptKey = DefaultCryptoKey.Value;
        }
    }
}
