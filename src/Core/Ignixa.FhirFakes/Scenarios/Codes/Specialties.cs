// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common practitioner specialty codes using SNOMED CT.
/// These codes represent medical specialties for healthcare practitioners.
/// </summary>
public static class Specialties
{
    /// <summary>
    /// Family medicine/General practice (419772000) - Primary care for all ages.
    /// </summary>
    public static readonly FhirCode FamilyMedicine = new(FhirCode.Systems.SnomedCt, "419772000", "Family medicine");

    /// <summary>
    /// Internal medicine (419192003) - Adult primary care and complex conditions.
    /// </summary>
    public static readonly FhirCode InternalMedicine = new(FhirCode.Systems.SnomedCt, "419192003", "Internal medicine");

    /// <summary>
    /// Pediatrics (394537008) - Medical care for children and adolescents.
    /// </summary>
    public static readonly FhirCode Pediatrics = new(FhirCode.Systems.SnomedCt, "394537008", "Pediatric medicine");

    /// <summary>
    /// Cardiology (394579002) - Heart and cardiovascular system.
    /// </summary>
    public static readonly FhirCode Cardiology = new(FhirCode.Systems.SnomedCt, "394579002", "Cardiology");

    /// <summary>
    /// Emergency medicine (773568002) - Acute and emergency care.
    /// </summary>
    public static readonly FhirCode EmergencyMedicine = new(FhirCode.Systems.SnomedCt, "773568002", "Emergency medicine");

    /// <summary>
    /// General surgery (394609007) - Surgical procedures and operations.
    /// </summary>
    public static readonly FhirCode GeneralSurgery = new(FhirCode.Systems.SnomedCt, "394609007", "General surgery");

    /// <summary>
    /// Obstetrics and gynecology (394586005) - Women's reproductive health.
    /// </summary>
    public static readonly FhirCode ObstetricsGynecology = new(FhirCode.Systems.SnomedCt, "394586005", "Obstetrics and gynecology");

    /// <summary>
    /// Psychiatry (394587001) - Mental health and behavioral disorders.
    /// </summary>
    public static readonly FhirCode Psychiatry = new(FhirCode.Systems.SnomedCt, "394587001", "Psychiatry");

    /// <summary>
    /// Neurology (394591006) - Nervous system disorders.
    /// </summary>
    public static readonly FhirCode Neurology = new(FhirCode.Systems.SnomedCt, "394591006", "Neurology");

    /// <summary>
    /// Orthopedic surgery (394801008) - Musculoskeletal system.
    /// </summary>
    public static readonly FhirCode OrthopedicSurgery = new(FhirCode.Systems.SnomedCt, "394801008", "Orthopedic surgery");

    /// <summary>
    /// Dermatology (394582007) - Skin conditions.
    /// </summary>
    public static readonly FhirCode Dermatology = new(FhirCode.Systems.SnomedCt, "394582007", "Dermatology");

    /// <summary>
    /// Ophthalmology (394594003) - Eye care and surgery.
    /// </summary>
    public static readonly FhirCode Ophthalmology = new(FhirCode.Systems.SnomedCt, "394594003", "Ophthalmology");

    /// <summary>
    /// Radiology (394914008) - Medical imaging and diagnostics.
    /// </summary>
    public static readonly FhirCode Radiology = new(FhirCode.Systems.SnomedCt, "394914008", "Radiology");

    /// <summary>
    /// Anesthesiology (394577000) - Anesthesia and pain management.
    /// </summary>
    public static readonly FhirCode Anesthesiology = new(FhirCode.Systems.SnomedCt, "394577000", "Anesthesiology");

    /// <summary>
    /// Pathology (394915009) - Laboratory medicine and diagnostics.
    /// </summary>
    public static readonly FhirCode Pathology = new(FhirCode.Systems.SnomedCt, "394915009", "Pathology");

    /// <summary>
    /// Oncology (394593009) - Cancer treatment.
    /// </summary>
    public static readonly FhirCode Oncology = new(FhirCode.Systems.SnomedCt, "394593009", "Oncology");

    /// <summary>
    /// Pulmonology (418112009) - Respiratory system.
    /// </summary>
    public static readonly FhirCode Pulmonology = new(FhirCode.Systems.SnomedCt, "418112009", "Pulmonology");

    /// <summary>
    /// Gastroenterology (394584008) - Digestive system.
    /// </summary>
    public static readonly FhirCode Gastroenterology = new(FhirCode.Systems.SnomedCt, "394584008", "Gastroenterology");

    /// <summary>
    /// Endocrinology (394583002) - Hormones and metabolism.
    /// </summary>
    public static readonly FhirCode Endocrinology = new(FhirCode.Systems.SnomedCt, "394583002", "Endocrinology");

    /// <summary>
    /// Nephrology (394589003) - Kidney care.
    /// </summary>
    public static readonly FhirCode Nephrology = new(FhirCode.Systems.SnomedCt, "394589003", "Nephrology");

    /// <summary>
    /// Urology (394612005) - Urinary system and male reproductive health.
    /// </summary>
    public static readonly FhirCode Urology = new(FhirCode.Systems.SnomedCt, "394612005", "Urology");

    /// <summary>
    /// Nursing (224535009) - Registered nurse care.
    /// </summary>
    public static readonly FhirCode Nursing = new(FhirCode.Systems.SnomedCt, "224535009", "Registered nurse");

    /// <summary>
    /// Nurse practitioner (224571005) - Advanced practice nursing.
    /// </summary>
    public static readonly FhirCode NursePractitioner = new(FhirCode.Systems.SnomedCt, "224571005", "Nurse practitioner");

    /// <summary>
    /// Physician assistant (449161006) - Physician assistant care.
    /// </summary>
    public static readonly FhirCode PhysicianAssistant = new(FhirCode.Systems.SnomedCt, "449161006", "Physician assistant");
}
