// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Describes the shape a city's postal code should be sampled in, so <see cref="DemographicsDataProvider.SampleZipCode"/>
/// can generate a realistically-shaped value instead of assuming a single (US) format for every country.
/// </summary>
public enum PostalCodeFormat
{
    /// <summary>US-style: append a 2-digit numeric suffix to <see cref="CityDemographics.ZipCodePrefix"/> (e.g. "021" -&gt; "02105").</summary>
    NumericSuffix,

    /// <summary>Fixed code, no suffix — the prefix already is the full code (e.g. Australian postcodes: "3000").</summary>
    FixedNumeric,

    /// <summary>Dutch-style: the prefix, a space, then 2 random uppercase letters (e.g. "1011" -&gt; "1011 AB").</summary>
    DutchAlphaNumeric,

    /// <summary>UK-style: the prefix (an alphanumeric outward code), a space, then a digit and 2 random uppercase letters (e.g. "SW1A" -&gt; "SW1A 1AA").</summary>
    UKAlphaNumeric
}
