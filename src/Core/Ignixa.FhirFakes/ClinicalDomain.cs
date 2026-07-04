// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes;

/// <summary>
/// Coarse clinical-specialty taxonomy used to keep generated codes thematically coherent.
/// Mirrors the SNOMED CT specialty vocabulary in Specialties.cs (practitioner-role entries
/// Nursing/NursePractitioner/PhysicianAssistant deliberately excluded - they are roles, not domains).
/// <see cref="Unspecified"/> (0) is the "no theme" sentinel.
/// </summary>
public enum ClinicalDomain
{
    Unspecified = 0,
    FamilyMedicine,
    InternalMedicine,
    Pediatrics,
    Cardiology,
    EmergencyMedicine,
    GeneralSurgery,
    ObstetricsGynecology,
    Psychiatry,
    Neurology,
    OrthopedicSurgery,
    Dermatology,
    Ophthalmology,
    Radiology,
    Anesthesiology,
    Pathology,
    Oncology,
    Pulmonology,
    Gastroenterology,
    Endocrinology,
    Nephrology,
    Urology,
}
