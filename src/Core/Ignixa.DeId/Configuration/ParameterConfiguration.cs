// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Legacy configuration model for de-identification parameters loaded from JSON config files.
/// </summary>
public class ParameterConfiguration
{
    [JsonPropertyName("dateShiftKey")]
    public string DateShiftKey { get; set; } = string.Empty;

    [JsonPropertyName("dateShiftScope")]
    public DateShiftScope DateShiftScope { get; set; }

    [JsonPropertyName("dateShiftFixedOffsetInDays")]
    public int? DateShiftFixedOffsetInDays { get; set; }

    [JsonPropertyName("cryptoHashKey")]
    public string CryptoHashKey { get; set; } = string.Empty;

    [JsonPropertyName("encryptKey")]
    public string EncryptKey { get; set; } = string.Empty;

    [JsonPropertyName("enablePartialAgesForRedact")]
    public bool EnablePartialAgesForRedact { get; set; }

    [JsonPropertyName("enablePartialDatesForRedact")]
    public bool EnablePartialDatesForRedact { get; set; }

    [JsonPropertyName("enablePartialZipCodesForRedact")]
    public bool EnablePartialZipCodesForRedact { get; set; }

    [JsonPropertyName("restrictedZipCodeTabulationAreas")]
    public List<string>? RestrictedZipCodeTabulationAreas { get; set; }

    [JsonPropertyName("customSettings")]
    public JsonObject? CustomSettings { get; set; }

    [JsonIgnore]
    public string DateShiftKeyPrefix { get; set; } = string.Empty;
}
