// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Constants for use with PatientBuilder to avoid magic strings.
/// Provides well-known values for demographics, geography, and clinical data.
/// </summary>
public static class PatientBuilderConstants
{
    /// <summary>
    /// FHIR AdministrativeGender values.
    /// </summary>
    public static class Gender
    {
        /// <summary>Male gender.</summary>
        public const string Male = "male";

        /// <summary>Female gender.</summary>
        public const string Female = "female";

        /// <summary>Other gender.</summary>
        public const string Other = "other";

        /// <summary>Unknown gender.</summary>
        public const string Unknown = "unknown";

        /// <summary>
        /// All administrative gender values (male, female, other, unknown).
        /// </summary>
        public static readonly string[] All = [Male, Female, Other, Unknown];

        /// <summary>
        /// Binary gender values (male, female) for demographic sampling.
        /// </summary>
        public static readonly string[] BinaryOnly = [Male, Female];
    }

    /// <summary>
    /// Available US cities in the demographics data provider.
    /// Each city has realistic census data for ethnicity, age, and gender distributions.
    /// </summary>
    public static class Cities
    {
        /// <summary>New York, New York (Population: 8.3M).</summary>
        public const string NewYork = "New York";

        /// <summary>Los Angeles, California (Population: 4.0M).</summary>
        public const string LosAngeles = "Los Angeles";

        /// <summary>Chicago, Illinois (Population: 2.7M).</summary>
        public const string Chicago = "Chicago";

        /// <summary>Houston, Texas (Population: 2.3M).</summary>
        public const string Houston = "Houston";

        /// <summary>Phoenix, Arizona (Population: 1.7M).</summary>
        public const string Phoenix = "Phoenix";

        /// <summary>Philadelphia, Pennsylvania (Population: 1.6M).</summary>
        public const string Philadelphia = "Philadelphia";

        /// <summary>San Antonio, Texas (Population: 1.5M).</summary>
        public const string SanAntonio = "San Antonio";

        /// <summary>San Diego, California (Population: 1.4M).</summary>
        public const string SanDiego = "San Diego";

        /// <summary>Dallas, Texas (Population: 1.3M).</summary>
        public const string Dallas = "Dallas";

        /// <summary>Boston, Massachusetts (Population: 676K).</summary>
        public const string Boston = "Boston";

        /// <summary>Seattle, Washington (added for test coverage, not in default demographics).</summary>
        public const string Seattle = "Seattle";
    }

    /// <summary>
    /// US state names corresponding to available cities.
    /// Use full state names (not abbreviations) with PatientBuilder.WithState() or .FromCity().
    /// </summary>
    public static class UsStates
    {
        /// <summary>New York state.</summary>
        public const string NewYork = "New York";

        /// <summary>California state.</summary>
        public const string California = "California";

        /// <summary>Illinois state.</summary>
        public const string Illinois = "Illinois";

        /// <summary>Texas state.</summary>
        public const string Texas = "Texas";

        /// <summary>Arizona state.</summary>
        public const string Arizona = "Arizona";

        /// <summary>Pennsylvania state.</summary>
        public const string Pennsylvania = "Pennsylvania";

        /// <summary>Massachusetts state.</summary>
        public const string Massachusetts = "Massachusetts";

        /// <summary>Washington state.</summary>
        public const string Washington = "Washington";

        /// <summary>Oregon state.</summary>
        public const string Oregon = "Oregon";

        /// <summary>Florida state.</summary>
        public const string Florida = "Florida";

        /// <summary>Ohio state.</summary>
        public const string Ohio = "Ohio";

        /// <summary>Georgia state.</summary>
        public const string Georgia = "Georgia";

        /// <summary>North Carolina state.</summary>
        public const string NorthCarolina = "North Carolina";

        /// <summary>Michigan state.</summary>
        public const string Michigan = "Michigan";
    }
}
