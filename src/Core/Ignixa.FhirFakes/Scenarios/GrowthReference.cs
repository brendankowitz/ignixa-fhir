// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Provides age-based anthropometric medians (height, weight, BMI) so generated vital signs are
/// clinically plausible across a patient's life, instead of a fixed adult-range random value.
/// </summary>
/// <remarks>
/// Values approximate the CDC/WHO 50th-percentile growth references, keyed by integer age (the
/// granularity at which wellness visits occur). Growth runs from birth to ~18, then plateaus.
/// Sex-specific where it matters (adolescence onward); unknown sex averages the two.
/// </remarks>
public static class GrowthReference
{
    // 50th-percentile median height (cm) by age 0..20. Index == age in years.
    private static readonly decimal[] MaleHeightCm =
        [50, 76, 88, 96, 103, 110, 116, 122, 128, 133, 138, 143, 149, 156, 164, 170, 173, 175, 176, 176, 177];

    private static readonly decimal[] FemaleHeightCm =
        [49, 74, 86, 94, 101, 108, 115, 121, 127, 133, 138, 144, 151, 157, 160, 162, 163, 163, 163, 163, 163];

    // 50th-percentile median weight (kg) by age 0..20. Index == age in years.
    private static readonly decimal[] MaleWeightKg =
        [3.5m, 10, 12.5m, 14, 16, 18, 20.5m, 23, 25.5m, 28.5m, 32, 36, 40, 45, 51, 56, 61, 65, 70, 74, 77];

    private static readonly decimal[] FemaleWeightKg =
        [3.4m, 9.5m, 12, 14, 16, 18, 20, 23, 26, 29, 33, 37, 42, 46, 50, 53, 55, 56, 57, 60, 63];

    private const int MaxTableAge = 20;

    /// <summary>Median height in centimetres for the given age and (optional) sex.</summary>
    public static decimal MedianHeightCm(int ageYears, string? sex = null) =>
        Lookup(MaleHeightCm, FemaleHeightCm, ageYears, sex);

    /// <summary>Median weight in kilograms for the given age and (optional) sex.</summary>
    public static decimal MedianWeightKg(int ageYears, string? sex = null) =>
        Lookup(MaleWeightKg, FemaleWeightKg, ageYears, sex);

    /// <summary>Median BMI (kg/m²) derived from the median height and weight for the age and sex.</summary>
    public static decimal MedianBmi(int ageYears, string? sex = null)
    {
        var heightM = MedianHeightCm(ageYears, sex) / 100m;
        return MedianWeightKg(ageYears, sex) / (heightM * heightM);
    }

    private static decimal Lookup(decimal[] male, decimal[] female, int ageYears, string? sex)
    {
        var index = Math.Clamp(ageYears, 0, MaxTableAge);
        return sex?.ToUpperInvariant() switch
        {
            "MALE" => male[index],
            "FEMALE" => female[index],
            _ => (male[index] + female[index]) / 2m
        };
    }
}
