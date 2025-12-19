// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Constants for IPS generation.
/// </summary>
public static class IpsConstants
{
    /// <summary>
    /// Default IPS Bundle profile URL.
    /// </summary>
    public const string DefaultBundleProfile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips";

    /// <summary>
    /// IPS Composition profile URL.
    /// </summary>
    public const string CompositionProfile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Composition-uv-ips";

    /// <summary>
    /// IPS Patient profile URL.
    /// </summary>
    public const string PatientProfile = "http://hl7.org/fhir/uv/ips/StructureDefinition/Patient-uv-ips";

    /// <summary>
    /// LOINC system URL.
    /// </summary>
    public const string LoincSystem = "http://loinc.org";

    /// <summary>
    /// Composition type code for Patient Summary Document.
    /// </summary>
    public const string CompositionTypeCode = "60591-5";

    /// <summary>
    /// Composition type display.
    /// </summary>
    public const string CompositionTypeDisplay = "Patient summary Document";

    /// <summary>
    /// Empty reason code system.
    /// </summary>
    public const string EmptyReasonSystem = "http://terminology.hl7.org/CodeSystem/list-empty-reason";

    /// <summary>
    /// Absent/unknown code system for IPS.
    /// </summary>
    public const string AbsentUnknownSystem = "http://hl7.org/fhir/uv/ips/CodeSystem/absent-unknown-uv-ips";

    /// <summary>
    /// LOINC section codes.
    /// </summary>
    public static class SectionCodes
    {
        public const string Allergies = "48765-2";
        public const string Medications = "10160-0";
        public const string Problems = "11450-4";
        public const string Immunizations = "11369-6";
        public const string Procedures = "47519-4";
        public const string Devices = "46264-8";
        public const string DiagnosticResults = "30954-2";
        public const string VitalSigns = "8716-3";
        public const string SocialHistory = "29762-2";
        public const string PregnancyHistory = "10162-6";
        public const string PastIllness = "11348-0";
        public const string FunctionalStatus = "47420-5";
        public const string PlanOfCare = "18776-5";
        public const string AdvanceDirectives = "42348-3";
    }
}
