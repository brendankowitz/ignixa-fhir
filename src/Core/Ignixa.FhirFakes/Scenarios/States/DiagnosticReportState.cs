// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a DiagnosticReport resource with associated Observations.
/// DiagnosticReports represent laboratory panels, imaging reports, and other diagnostic studies.
/// </summary>
public sealed class DiagnosticReportState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the diagnostic report code (from DiagnosticReports constants).
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the report status ("registered", "partial", "preliminary", "final", "amended", "corrected", "appended", "cancelled", "entered-in-error", "unknown").
    /// </summary>
    public string Status { get; init; } = "final";

    /// <summary>
    /// Gets or sets the category of the diagnostic report ("LAB", "RAD", "PATH", etc.).
    /// If not specified, it is inferred from the code.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets or sets the observations included in this report.
    /// Each tuple contains (observation code, value, unit).
    /// </summary>
    public IReadOnlyList<(FhirCode Code, decimal Value, string Unit)>? Observations { get; init; }

    /// <summary>
    /// Gets the StateIds of observations to reference in this diagnostic report.
    /// These observations must have been created with StateId in a previous AddState() call.
    /// Allows referencing existing observations without creating duplicates.
    /// </summary>
    public IReadOnlyList<string>? ReferencedObservationStateIds { get; init; }

    /// <summary>
    /// Gets or sets the conclusion text for imaging reports.
    /// </summary>
    public string? Conclusion { get; init; }

    /// <summary>
    /// Gets or sets the performer name for the report.
    /// </summary>
    public string? PerformerName { get; init; }

    /// <summary>
    /// Gets or sets whether this is an imaging report (true) or lab report (false).
    /// If not specified, it is inferred from the code.
    /// </summary>
    public bool? IsImagingReport { get; init; }

    /// <summary>
    /// Creates a DiagnosticReport resource with associated Observation resources.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create DiagnosticReport without a Patient. Ensure InitialState runs first.");
        }

        var report = faker.Generate("DiagnosticReport");
        var node = report.MutableNode;
        var reportId = Guid.NewGuid().ToString();

        // Set required fields
        node["id"] = reportId;
        node["status"] = Status;

        // Determine and set category
        var isImaging = IsImagingReport ?? InferIsImagingReport();
        var categoryCode = Category ?? (isImaging ? "RAD" : "LAB");
        var categoryDisplay = isImaging ? "Radiology" : "Laboratory";

        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/v2-0074",
                        ["code"] = categoryCode,
                        ["display"] = categoryDisplay
                    }
                }
            }
        };

        // Set report code
        node["code"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = Code.System,
                    ["code"] = Code.Code,
                    ["display"] = Code.Display
                }
            },
            ["text"] = Code.Display
        };

        // Set patient reference
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set encounter reference if available
        if (context.CurrentEncounter is not null)
        {
            node["encounter"] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }

        // Set effective date
        node["effectiveDateTime"] = context.CurrentTime.ToString("o");

        // Set issued date (when report was made available)
        node["issued"] = context.CurrentTime.AddMinutes(_faker.Random.Int(5, 120)).ToString("o");

        // Set performer
        var performers = new JsonArray();

        // Add practitioner reference if available
        if (context.CurrentPractitioner is not null)
        {
            performers.Add(new JsonObject
            {
                ["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}"
            });
        }

        // Add organization reference if available
        if (context.CurrentOrganization is not null)
        {
            performers.Add(new JsonObject
            {
                ["reference"] = $"Organization/{context.CurrentOrganization.Id}"
            });
        }

        // If no practitioner or organization, add display name
        if (performers.Count == 0)
        {
            var performerName = PerformerName ?? GeneratePerformerName(isImaging);
            performers.Add(new JsonObject
            {
                ["display"] = performerName
            });
        }

        node["performer"] = performers;

        // Create result array for observation references
        var observationReferences = new JsonArray();

        // Add references to existing observations by StateId
        if (ReferencedObservationStateIds is not null)
        {
            foreach (var stateId in ReferencedObservationStateIds)
            {
                var observation = context.GetStateResource(stateId);
                if (observation is not null)
                {
                    observationReferences.Add(new JsonObject
                    {
                        ["reference"] = $"Observation/{observation.Id}"
                    });
                }
            }
        }

        // Create new observations from tuples (existing behavior)
        if (Observations is { Count: > 0 })
        {
            foreach (var (obsCode, obsValue, obsUnit) in Observations)
            {
                var observation = CreateObservation(context, faker, obsCode, obsValue, obsUnit);
                context.AddObservation(observation, obsCode.Display);

                observationReferences.Add(new JsonObject
                {
                    ["reference"] = $"Observation/{observation.Id}"
                });
            }
        }

        // Set result references
        if (observationReferences.Count > 0)
        {
            node["result"] = observationReferences;
        }

        // Set conclusion for imaging reports
        if (!string.IsNullOrEmpty(Conclusion))
        {
            node["conclusion"] = Conclusion;
        }

        // Add to context
        context.AddDiagnosticReport(report, Code.Display);

        // NEW: Register with StateId for cross-references
        context.RegisterStateResource(StateId, report);
    }

    private bool InferIsImagingReport()
    {
        // Imaging reports typically have certain LOINC codes or contain imaging-related terms
        var imagingTerms = new[] { "X-ray", "CT", "MRI", "MG", "US ", "Ultrasound", "Mammography", "EKG", "ECG", "Echocardiography" };
        return imagingTerms.Any(term => Code.Display.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GeneratePerformerName(bool isImaging)
    {
        var specialties = isImaging
            ? new[] { "Radiology", "Diagnostic Imaging", "Nuclear Medicine" }
            : new[] { "Pathology", "Clinical Laboratory", "Medical Laboratory" };

        var specialty = specialties[_faker.Random.Int(0, specialties.Length - 1)];
        return $"{specialty} Department - {_faker.Name.FullName()}, MD";
    }

    private static Ignixa.Serialization.SourceNodes.ResourceJsonNode CreateObservation(
        ScenarioContext context,
        SchemaBasedFhirResourceFaker faker,
        FhirCode code,
        decimal value,
        string unit)
    {
        var observation = faker.Generate("Observation");
        var node = observation.MutableNode;

        node["id"] = Guid.NewGuid().ToString();
        node["status"] = "final";

        // Set category as laboratory
        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/observation-category",
                        ["code"] = "laboratory",
                        ["display"] = "Laboratory"
                    }
                }
            }
        };

        // Set code
        node["code"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = code.System,
                    ["code"] = code.Code,
                    ["display"] = code.Display
                }
            },
            ["text"] = code.Display
        };

        // Set patient reference
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient!.Id}"
        };

        // Set encounter reference if available
        if (context.CurrentEncounter is not null)
        {
            node["encounter"] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }

        // Set effective date
        node["effectiveDateTime"] = context.CurrentTime.ToString("o");

        // Set value
        node["valueQuantity"] = new JsonObject
        {
            ["value"] = value,
            ["unit"] = unit,
            ["system"] = FhirCode.Systems.Ucum,
            ["code"] = unit
        };

        return observation;
    }

    #region Factory Methods

    /// <summary>
    /// Creates a Basic Metabolic Panel (BMP) diagnostic report with standard lab values (8 tests).
    /// </summary>
    public static DiagnosticReportState BasicMetabolicPanel() => new()
    {
        Code = DiagnosticReports.BasicMetabolicPanel,
        Observations =
        [
            (LabObservations.Glucose, 95, "mg/dL"),
            (LabObservations.BUN, 15, "mg/dL"),
            (LabObservations.Creatinine, 1.0m, "mg/dL"),
            (LabObservations.Sodium, 140, "mmol/L"),
            (LabObservations.Potassium, 4.2m, "mmol/L"),
            (LabObservations.Chloride, 101, "mmol/L"),
            (LabObservations.CarbonDioxide, 25, "mmol/L"),
            (LabObservations.Calcium, 9.5m, "mg/dL")
        ]
    };

    /// <summary>
    /// Creates a Comprehensive Metabolic Panel (CMP) diagnostic report with standard lab values.
    /// </summary>
    public static DiagnosticReportState ComprehensiveMetabolicPanel() => new()
    {
        Code = DiagnosticReports.ComprehensiveMetabolicPanel,
        Observations =
        [
            (LabObservations.Glucose, 95, "mg/dL"),
            (LabObservations.Sodium, 140, "mmol/L"),
            (LabObservations.Potassium, 4.2m, "mmol/L"),
            (LabObservations.Chloride, 101, "mmol/L"),
            (LabObservations.CarbonDioxide, 25, "mmol/L"),
            (LabObservations.BUN, 15, "mg/dL"),
            (LabObservations.Creatinine, 1.0m, "mg/dL"),
            (LabObservations.Calcium, 9.5m, "mg/dL"),
            (LabObservations.TotalProtein, 7.0m, "g/dL"),
            (LabObservations.Albumin, 4.2m, "g/dL"),
            (LabObservations.BilirubinTotal, 0.8m, "mg/dL"),
            (LabObservations.AlkalinePhosphatase, 70, "U/L"),
            (LabObservations.ALT, 25, "U/L"),
            (LabObservations.AST, 22, "U/L")
        ]
    };

    /// <summary>
    /// Creates a Complete Blood Count (CBC) diagnostic report with standard values.
    /// </summary>
    public static DiagnosticReportState CompleteBloodCount() => new()
    {
        Code = DiagnosticReports.CompleteBloodCount,
        Observations =
        [
            (LabObservations.Hemoglobin, 14.5m, "g/dL"),
            (LabObservations.Hematocrit, 43, "%"),
            (LabObservations.WBC, 7.5m, "10*3/uL"),
            (LabObservations.RBC, 5.0m, "10*6/uL"),
            (LabObservations.Platelets, 250, "10*3/uL"),
            (LabObservations.MCV, 86, "fL"),
            (LabObservations.MCH, 29, "pg"),
            (LabObservations.MCHC, 34, "g/dL")
        ]
    };

    /// <summary>
    /// Creates a Lipid Panel diagnostic report with standard values.
    /// </summary>
    public static DiagnosticReportState LipidPanel() => new()
    {
        Code = DiagnosticReports.LipidPanel,
        Observations =
        [
            (LabObservations.TotalCholesterol, 185, "mg/dL"),
            (LabObservations.HDLCholesterol, 55, "mg/dL"),
            (LabObservations.LDLCholesterol, 110, "mg/dL"),
            (LabObservations.Triglycerides, 120, "mg/dL")
        ]
    };

    /// <summary>
    /// Creates a Chest X-ray diagnostic report.
    /// </summary>
    public static DiagnosticReportState ChestXRay(string? conclusion = null) => new()
    {
        Code = DiagnosticReports.ChestXRay,
        IsImagingReport = true,
        Conclusion = conclusion ?? "No acute cardiopulmonary abnormality. Lungs are clear. Heart size is normal."
    };

    /// <summary>
    /// Creates a CT Head diagnostic report.
    /// </summary>
    public static DiagnosticReportState CTHeadWithoutContrast(string? conclusion = null) => new()
    {
        Code = DiagnosticReports.CTHeadWoContrast,
        IsImagingReport = true,
        Conclusion = conclusion ?? "No acute intracranial abnormality. No evidence of acute hemorrhage, mass effect, or midline shift."
    };

    /// <summary>
    /// Creates an MRI Brain diagnostic report.
    /// </summary>
    public static DiagnosticReportState MRIBrainWithoutContrast(string? conclusion = null) => new()
    {
        Code = DiagnosticReports.MRIBrainWoContrast,
        IsImagingReport = true,
        Conclusion = conclusion ?? "Unremarkable MRI of the brain. No evidence of acute infarct, mass, or abnormal enhancement."
    };

    /// <summary>
    /// Creates an Abdominal Ultrasound diagnostic report.
    /// </summary>
    public static DiagnosticReportState AbdominalUltrasound(string? conclusion = null) => new()
    {
        Code = DiagnosticReports.UltrasoundAbdomen,
        IsImagingReport = true,
        Conclusion = conclusion ?? "Normal abdominal ultrasound. Liver, gallbladder, pancreas, spleen, and kidneys are unremarkable."
    };

    #endregion
}
