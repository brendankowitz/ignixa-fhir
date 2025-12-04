// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Selector classes for fluent PatientBuilder API.
/// These enable discoverable constants through IntelliSense: .WithGender(g => g.Male)
/// </summary>
public static class PatientBuilderSelectors
{
    /// <summary>
    /// Gender selector for use with PatientBuilder.WithGender().
    /// </summary>
    public sealed class Gender
    {
        /// <summary>Male gender.</summary>
        public string Male => PatientBuilderConstants.Gender.Male;

        /// <summary>Female gender.</summary>
        public string Female => PatientBuilderConstants.Gender.Female;

        /// <summary>Other gender.</summary>
        public string Other => PatientBuilderConstants.Gender.Other;

        /// <summary>Unknown gender.</summary>
        public string Unknown => PatientBuilderConstants.Gender.Unknown;
    }

    /// <summary>
    /// Race selector for use with PatientBuilder.WithRace().
    /// </summary>
    public sealed class Race
    {
        /// <summary>White race category.</summary>
        public string White => PatientBuilderConstants.Race.White;

        /// <summary>Black or African American race category.</summary>
        public string Black => PatientBuilderConstants.Race.Black;

        /// <summary>Hispanic or Latino race category.</summary>
        public string Hispanic => PatientBuilderConstants.Race.Hispanic;

        /// <summary>Asian race category (general).</summary>
        public string Asian => PatientBuilderConstants.Race.Asian;

        /// <summary>Asian - Chinese ethnicity.</summary>
        public string AsianChinese => PatientBuilderConstants.Race.AsianChinese;

        /// <summary>Asian - Indian ethnicity.</summary>
        public string AsianIndian => PatientBuilderConstants.Race.AsianIndian;

        /// <summary>Asian - Filipino ethnicity.</summary>
        public string AsianFilipino => PatientBuilderConstants.Race.AsianFilipino;

        /// <summary>Asian - Vietnamese ethnicity.</summary>
        public string AsianVietnamese => PatientBuilderConstants.Race.AsianVietnamese;

        /// <summary>Asian - Korean ethnicity.</summary>
        public string AsianKorean => PatientBuilderConstants.Race.AsianKorean;

        /// <summary>Asian - Japanese ethnicity.</summary>
        public string AsianJapanese => PatientBuilderConstants.Race.AsianJapanese;

        /// <summary>Native American or Alaska Native race category.</summary>
        public string NativeAmerican => PatientBuilderConstants.Race.NativeAmerican;

        /// <summary>Native Hawaiian or Other Pacific Islander race category.</summary>
        public string PacificIslander => PatientBuilderConstants.Race.PacificIslander;

        /// <summary>Arab race category.</summary>
        public string Arab => PatientBuilderConstants.Race.Arab;

        /// <summary>Other race category.</summary>
        public string Other => PatientBuilderConstants.Race.Other;
    }

    /// <summary>
    /// City-state pair selector for use with PatientBuilder.FromCity().
    /// </summary>
    [Obsolete("Use KnownCities class instead. This class is maintained for backward compatibility only.")]
    public sealed class CityState
    {
        /// <summary>New York, New York (Population: 8.3M).</summary>
        public (string City, string State) NewYorkNY => PatientBuilderConstants.CityStatePairs.NewYorkNY;

        /// <summary>Los Angeles, California (Population: 4.0M).</summary>
        public (string City, string State) LosAngelesCA => PatientBuilderConstants.CityStatePairs.LosAngelesCA;

        /// <summary>Chicago, Illinois (Population: 2.7M).</summary>
        public (string City, string State) ChicagoIL => PatientBuilderConstants.CityStatePairs.ChicagoIL;

        /// <summary>Houston, Texas (Population: 2.3M).</summary>
        public (string City, string State) HoustonTX => PatientBuilderConstants.CityStatePairs.HoustonTX;

        /// <summary>Phoenix, Arizona (Population: 1.7M).</summary>
        public (string City, string State) PhoenixAZ => PatientBuilderConstants.CityStatePairs.PhoenixAZ;

        /// <summary>Philadelphia, Pennsylvania (Population: 1.6M).</summary>
        public (string City, string State) PhiladelphiaPA => PatientBuilderConstants.CityStatePairs.PhiladelphiaPA;

        /// <summary>San Antonio, Texas (Population: 1.5M).</summary>
        public (string City, string State) SanAntonioTX => PatientBuilderConstants.CityStatePairs.SanAntonioTX;

        /// <summary>San Diego, California (Population: 1.4M).</summary>
        public (string City, string State) SanDiegoCA => PatientBuilderConstants.CityStatePairs.SanDiegoCA;

        /// <summary>Dallas, Texas (Population: 1.3M).</summary>
        public (string City, string State) DallasTX => PatientBuilderConstants.CityStatePairs.DallasTX;

        /// <summary>Boston, Massachusetts (Population: 676K).</summary>
        public (string City, string State) BostonMA => PatientBuilderConstants.CityStatePairs.BostonMA;
    }
}
