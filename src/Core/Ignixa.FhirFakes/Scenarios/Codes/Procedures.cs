// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common medical and surgical procedure codes (SNOMED CT).
/// Organized by specialty: General Surgery, Cardiology, Orthopedics, Gastroenterology, and more.
/// </summary>
public static class Procedures
{
    // General Surgery

    /// <summary>Appendectomy - Surgical removal of the appendix</summary>
    public static FhirCode Appendectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "80146002",
        Display: "Appendectomy") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Cholecystectomy - Surgical removal of the gallbladder</summary>
    public static FhirCode Cholecystectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "38102005",
        Display: "Cholecystectomy") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Hernia repair - Surgical repair of a hernia</summary>
    public static FhirCode HerniaRepair { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "48387007",
        Display: "Hernia repair") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Mastectomy - Surgical removal of breast tissue</summary>
    public static FhirCode Mastectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "172043006",
        Display: "Mastectomy") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Tonsillectomy - Surgical removal of tonsils</summary>
    public static FhirCode Tonsillectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "173422009",
        Display: "Tonsillectomy") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Thyroidectomy - Surgical removal of thyroid gland</summary>
    public static FhirCode Thyroidectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "15993003",
        Display: "Thyroidectomy") { Domain = ClinicalDomain.GeneralSurgery };

    // Cardiology & Vascular

    /// <summary>Cardiac catheterization - Invasive procedure to examine heart function</summary>
    public static FhirCode CardiacCatheterization { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "41976001",
        Display: "Cardiac catheterization") { Domain = ClinicalDomain.Cardiology };

    /// <summary>Coronary artery bypass graft (CABG) - Heart bypass surgery</summary>
    public static FhirCode CABG { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "232717009",
        Display: "Coronary artery bypass grafting") { Domain = ClinicalDomain.Cardiology };

    /// <summary>Percutaneous coronary intervention (PCI) - Angioplasty with stent placement</summary>
    public static FhirCode PCI { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "415070008",
        Display: "Percutaneous coronary intervention") { Domain = ClinicalDomain.Cardiology };

    /// <summary>Pacemaker insertion - Implantation of cardiac pacemaker</summary>
    public static FhirCode PacemakerInsertion { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "25267002",
        Display: "Insertion of cardiac pacemaker") { Domain = ClinicalDomain.Cardiology };

    /// <summary>Cardioversion - Restoration of normal heart rhythm</summary>
    public static FhirCode Cardioversion { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "180325003",
        Display: "Cardioversion") { Domain = ClinicalDomain.Cardiology };

    // Orthopedic Surgery

    /// <summary>Total hip replacement - Arthroplasty of hip joint</summary>
    public static FhirCode TotalHipReplacement { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "52734007",
        Display: "Total hip replacement") { Domain = ClinicalDomain.OrthopedicSurgery };

    /// <summary>Total knee replacement - Arthroplasty of knee joint</summary>
    public static FhirCode TotalKneeReplacement { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "609588000",
        Display: "Total knee replacement") { Domain = ClinicalDomain.OrthopedicSurgery };

    /// <summary>Spinal fusion - Surgical fusion of vertebrae</summary>
    public static FhirCode SpinalFusion { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "81723002",
        Display: "Spinal fusion") { Domain = ClinicalDomain.OrthopedicSurgery };

    /// <summary>Arthroscopy of knee - Minimally invasive knee surgery</summary>
    public static FhirCode ArthroscopyKnee { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "386729009",
        Display: "Arthroscopy of knee") { Domain = ClinicalDomain.OrthopedicSurgery };

    /// <summary>Open reduction and internal fixation (ORIF) - Fracture repair with hardware</summary>
    public static FhirCode ORIF { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "67404009",
        Display: "Open reduction of fracture with internal fixation") { Domain = ClinicalDomain.OrthopedicSurgery };

    // Gastroenterology

    /// <summary>Colonoscopy - Endoscopic examination of the colon</summary>
    public static FhirCode Colonoscopy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "73761001",
        Display: "Colonoscopy") { Domain = ClinicalDomain.Gastroenterology };

    /// <summary>Upper endoscopy (EGD) - Esophagogastroduodenoscopy</summary>
    public static FhirCode UpperEndoscopy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "1255005",
        Display: "Esophagogastroduodenoscopy") { Domain = ClinicalDomain.Gastroenterology };

    /// <summary>ERCP - Endoscopic retrograde cholangiopancreatography</summary>
    public static FhirCode ERCP { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "10306003",
        Display: "Endoscopic retrograde cholangiopancreatography") { Domain = ClinicalDomain.Gastroenterology };

    /// <summary>Colectomy - Surgical removal of colon</summary>
    public static FhirCode Colectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "26390003",
        Display: "Colectomy") { Domain = ClinicalDomain.Gastroenterology };

    // Obstetrics & Gynecology

    /// <summary>Cesarean section - Surgical delivery of baby</summary>
    public static FhirCode CesareanSection { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "11466000",
        Display: "Cesarean section") { Domain = ClinicalDomain.ObstetricsGynecology };

    /// <summary>Hysterectomy - Surgical removal of uterus</summary>
    public static FhirCode Hysterectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "236886002",
        Display: "Hysterectomy") { Domain = ClinicalDomain.ObstetricsGynecology };

    /// <summary>Dilation and curettage (D&amp;C) - Uterine scraping procedure</summary>
    public static FhirCode DilationAndCurettage { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "33130005",
        Display: "Dilation and curettage") { Domain = ClinicalDomain.ObstetricsGynecology };

    // Urology

    /// <summary>Cystoscopy - Endoscopic examination of bladder</summary>
    public static FhirCode Cystoscopy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "27310002",
        Display: "Cystoscopy") { Domain = ClinicalDomain.Urology };

    /// <summary>Prostatectomy - Surgical removal of prostate gland</summary>
    public static FhirCode Prostatectomy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "176258007",
        Display: "Prostatectomy") { Domain = ClinicalDomain.Urology };

    /// <summary>Kidney stone removal (lithotripsy) - Extracorporeal shock wave lithotripsy</summary>
    public static FhirCode Lithotripsy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "60964009",
        Display: "Extracorporeal shockwave lithotripsy") { Domain = ClinicalDomain.Urology };

    // Imaging Procedures

    /// <summary>CT scan - Computed tomography imaging</summary>
    public static FhirCode CTScan { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "77477000",
        Display: "Computed tomography") { Domain = ClinicalDomain.Radiology };

    /// <summary>MRI scan - Magnetic resonance imaging</summary>
    public static FhirCode MRIScan { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "113091000",
        Display: "Magnetic resonance imaging") { Domain = ClinicalDomain.Radiology };

    /// <summary>X-ray - Radiographic imaging</summary>
    public static FhirCode XRay { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "168537006",
        Display: "Plain radiography") { Domain = ClinicalDomain.Radiology };

    /// <summary>Ultrasound - Ultrasonographic imaging</summary>
    public static FhirCode Ultrasound { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "16310003",
        Display: "Ultrasonography") { Domain = ClinicalDomain.Radiology };

    /// <summary>PET scan - Positron emission tomography</summary>
    public static FhirCode PETScan { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "82918005",
        Display: "Positron emission tomography") { Domain = ClinicalDomain.Radiology };

    /// <summary>Mammography - X-ray imaging of breast</summary>
    public static FhirCode Mammography { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "71651007",
        Display: "Mammography") { Domain = ClinicalDomain.Radiology };

    // Other Common Procedures

    /// <summary>Biopsy - Tissue sample collection</summary>
    public static FhirCode Biopsy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "86273004",
        Display: "Biopsy") { Domain = ClinicalDomain.Pathology };

    /// <summary>Blood transfusion - Infusion of blood products</summary>
    public static FhirCode BloodTransfusion { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "116859006",
        Display: "Transfusion of blood product") { Domain = ClinicalDomain.InternalMedicine };

    /// <summary>Incision and drainage - Abscess drainage</summary>
    public static FhirCode IncisionAndDrainage { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "392235006",
        Display: "Incision and drainage") { Domain = ClinicalDomain.GeneralSurgery };

    /// <summary>Intubation - Endotracheal tube insertion</summary>
    public static FhirCode Intubation { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "112798008",
        Display: "Insertion of endotracheal tube") { Domain = ClinicalDomain.EmergencyMedicine };

    /// <summary>Mechanical ventilation - Assisted breathing support</summary>
    public static FhirCode MechanicalVentilation { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "40617009",
        Display: "Artificial respiration") { Domain = ClinicalDomain.EmergencyMedicine };

    /// <summary>Central venous catheter insertion - Central line placement</summary>
    public static FhirCode CentralVenousCatheter { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "392247006",
        Display: "Insertion of central venous catheter") { Domain = ClinicalDomain.EmergencyMedicine };
}
