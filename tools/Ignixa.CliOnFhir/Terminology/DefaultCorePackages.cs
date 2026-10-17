// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Terminology.Npm;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>The FHIR core package loaded when no terminology package is configured, per FHIR version.</summary>
public static class DefaultCorePackages
{
    /// <summary>The core package of <paramref name="version"/>; null for a version without a published one.</summary>
    public static PackageRequest? For(FhirVersion version) => version switch
    {
        FhirVersion.Stu3 => PackageRequest.Exact("hl7.fhir.r3.core", "3.0.2"),
        FhirVersion.R4 => PackageRequest.Exact("hl7.fhir.r4.core", "4.0.1"),
        FhirVersion.R4B => PackageRequest.Exact("hl7.fhir.r4b.core", "4.3.0"),
        FhirVersion.R5 => PackageRequest.Exact("hl7.fhir.r5.core", "5.0.0"),
        _ => null,
    };

    /// <summary>The HL7 Terminology (THO) package flavour that matches <paramref name="version"/>, named in tool descriptions.</summary>
    public static string TerminologyPackageFor(FhirVersion version) => version is FhirVersion.R5 or FhirVersion.R6 ? "hl7.terminology.r5" : "hl7.terminology.r4";
}
