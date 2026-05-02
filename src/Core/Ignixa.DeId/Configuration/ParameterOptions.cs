// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Parameters for de-identification processors (keys, scopes, etc.).
/// </summary>
public sealed record ParameterOptions
{
    [JsonPropertyName("dateShiftKey")]
    public string? DateShiftKey { get; init; }

    [JsonPropertyName("dateShiftScope")]
    public DateShiftScope? DateShiftScope { get; init; }

    [JsonPropertyName("dateShiftFixedOffsetInDays")]
    public int? DateShiftFixedOffsetInDays { get; init; }

    [JsonPropertyName("cryptoHashKey")]
    public string? CryptoHashKey { get; init; }

    [JsonPropertyName("encryptKey")]
    public string? EncryptKey { get; init; }

    [JsonPropertyName("enablePartialAgesForRedact")]
    public bool EnablePartialAgesForRedact { get; init; }

    [JsonPropertyName("enablePartialDatesForRedact")]
    public bool EnablePartialDatesForRedact { get; init; }

    [JsonPropertyName("enablePartialZipCodesForRedact")]
    public bool EnablePartialZipCodesForRedact { get; init; }

    [JsonPropertyName("restrictedZipCodeTabulationAreas")]
    public ImmutableArray<string>? RestrictedZipCodeTabulationAreas { get; init; }
}
