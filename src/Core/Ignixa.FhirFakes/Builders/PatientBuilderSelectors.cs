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
}
