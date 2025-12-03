// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a ServiceRequest resource.
/// ServiceRequests represent orders for diagnostic tests, imaging studies, procedures, and specialist consultations.
/// Used to test workflow state transitions, task management, and fulfillment scenarios.
/// </summary>
/// <remarks>
/// <para>
/// ServiceRequest is a critical resource in FHIR for representing:
/// <list type="bullet">
/// <item><description>Laboratory orders (e.g., CBC, lipid panel, hemoglobin A1c)</description></item>
/// <item><description>Imaging orders (e.g., chest X-ray, CT scan, MRI)</description></item>
/// <item><description>Specialist referrals (e.g., cardiology consult, orthopedic referral)</description></item>
/// <item><description>Procedure requests (e.g., colonoscopy, biopsy)</description></item>
/// </list>
/// </para>
/// <para>
/// The resource supports various intents (proposal, plan, order) and priorities (routine, urgent, stat)
/// to model real-world clinical workflows.
/// </para>
/// </remarks>
public sealed class ServiceRequestState : ScenarioState
{
    /// <summary>
    /// Gets the service being requested (LOINC for labs, SNOMED for procedures/imaging).
    /// </summary>
    /// <remarks>
    /// Use <see cref="ServiceRequestCodes"/> for common laboratory, imaging, and referral codes.
    /// </remarks>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the request status.
    /// Valid values: "draft", "active", "on-hold", "revoked", "completed", "entered-in-error", "unknown".
    /// </summary>
    /// <remarks>
    /// Default is "active" indicating the request is actionable and ready for fulfillment.
    /// </remarks>
    public string Status { get; init; } = "active";

    /// <summary>
    /// Gets or sets the intent of the request.
    /// Valid values: "proposal", "plan", "directive", "order", "original-order", "reflex-order", "filler-order", "instance-order", "option".
    /// </summary>
    /// <remarks>
    /// Default is "order" indicating an authoritative instruction to fulfill the request.
    /// </remarks>
    public string Intent { get; init; } = "order";

    /// <summary>
    /// Gets or sets the priority of the request.
    /// Valid values: "routine", "urgent", "asap", "stat".
    /// </summary>
    /// <remarks>
    /// Default is "routine" for standard turnaround time.
    /// Use "stat" for emergency/life-threatening situations.
    /// </remarks>
    public string Priority { get; init; } = "routine";

    /// <summary>
    /// Gets or sets the category of service request.
    /// Common values: "laboratory", "imaging", "referral", "counseling", "procedure".
    /// </summary>
    /// <remarks>
    /// Uses http://hl7.org/fhir/ValueSet/servicerequest-category codes.
    /// If not specified, the category is inferred from the service code.
    /// </remarks>
    public string? Category { get; init; }

    /// <summary>
    /// Gets or sets when the request was created.
    /// </summary>
    /// <remarks>
    /// If not specified, defaults to the scenario's current simulation time.
    /// </remarks>
    public DateTime? AuthoredOn { get; init; }

    /// <summary>
    /// Gets or sets when the service should occur.
    /// </summary>
    /// <remarks>
    /// Optional. When set, indicates the scheduled or desired time for service delivery.
    /// </remarks>
    public DateTime? OccurrenceDateTime { get; init; }

    /// <summary>
    /// Gets or sets the human-readable reason for the request.
    /// </summary>
    /// <remarks>
    /// Use for free-text clinical reasons when a coded reason is not available.
    /// </remarks>
    public string? ReasonDisplay { get; init; }

    /// <summary>
    /// Gets or sets the coded reason for the request.
    /// </summary>
    /// <remarks>
    /// Typically a condition or diagnosis code (e.g., from <see cref="FhirCode.Conditions"/>).
    /// </remarks>
    public FhirCode? ReasonCode { get; init; }

    /// <summary>
    /// Gets or sets the attribute name of the condition this request is for.
    /// </summary>
    /// <remarks>
    /// When set, the Execute method looks up the condition ID from context attributes
    /// and creates a reasonReference to that Condition resource.
    /// </remarks>
    public string? ReasonConditionAttribute { get; init; }

    /// <summary>
    /// Gets or sets optional notes for the service request.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Gets or sets the performer reference display name.
    /// </summary>
    /// <remarks>
    /// If not specified, uses the current organization or practitioner from context.
    /// </remarks>
    public string? PerformerDisplay { get; init; }

    /// <summary>
    /// Creates a ServiceRequest resource linked to the patient and current context.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    /// <exception cref="InvalidOperationException">Thrown when no Patient exists in the context.</exception>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create ServiceRequest without a Patient. Ensure InitialState runs first.");
        }

        var serviceRequest = faker.Generate("ServiceRequest");
        var node = serviceRequest.MutableNode;

        // Set resource ID
        node["id"] = Guid.NewGuid().ToString();

        // Set identifier
        node["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "urn:oid:1.2.3.4.5.6.7",
                ["value"] = $"SR-{Guid.NewGuid():N}"[..16]
            }
        };

        // Set required fields
        node["status"] = Status;
        node["intent"] = Intent;
        node["priority"] = Priority;

        // Set category
        var categoryValue = Category ?? InferCategory();
        if (!string.IsNullOrEmpty(categoryValue))
        {
            node["category"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = "http://snomed.info/sct",
                            ["code"] = MapCategoryToCode(categoryValue),
                            ["display"] = MapCategoryToDisplay(categoryValue)
                        }
                    },
                    ["text"] = MapCategoryToDisplay(categoryValue)
                }
            };
        }

        // Set the service code
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

        // Set subject (patient reference)
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

        // Set requester if practitioner is available
        if (context.CurrentPractitioner is not null)
        {
            node["requester"] = new JsonObject
            {
                ["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}"
            };
        }

        // Set performer
        SetPerformer(node, context);

        // Set authoredOn
        node["authoredOn"] = (AuthoredOn ?? context.CurrentTime).ToString("o");

        // Set occurrenceDateTime if specified
        if (OccurrenceDateTime.HasValue)
        {
            node["occurrenceDateTime"] = OccurrenceDateTime.Value.ToString("o");
        }

        // Set reason (code or reference)
        SetReason(node, context);

        // Set note if provided
        if (!string.IsNullOrEmpty(Note))
        {
            node["note"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = Note,
                    ["time"] = context.CurrentTime.ToString("o")
                }
            };
        }

        // Add to context
        context.AddServiceRequest(serviceRequest, Code.Display);
    }

    private string InferCategory()
    {
        var system = Code.System;
        var display = Code.Display.ToUpperInvariant();

        // LOINC codes are typically laboratory
        if (system == FhirCode.Systems.Loinc)
        {
            return "laboratory";
        }

        // Check display text for imaging keywords
        if (display.Contains("X-RAY", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("CT ", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("ULTRASOUND", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("MAMMOG", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("SCAN", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("IMAGING", StringComparison.OrdinalIgnoreCase))
        {
            return "imaging";
        }

        // Check for referral/consult keywords
        if (display.Contains("CONSULT", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("REFERRAL", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("REFER", StringComparison.OrdinalIgnoreCase))
        {
            return "referral";
        }

        // Check for therapy keywords
        if (display.Contains("THERAPY", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("REHABILITATION", StringComparison.OrdinalIgnoreCase))
        {
            return "counseling";
        }

        // Default to procedure for SNOMED CT codes
        return "procedure";
    }

    private static string MapCategoryToCode(string category) => category.ToUpperInvariant() switch
    {
        "LABORATORY" => "108252007",
        "IMAGING" => "363679005",
        "REFERRAL" => "3457005",
        "COUNSELING" => "409063005",
        "EDUCATION" => "409073007",
        "SURGICAL" or "SURGERY" => "387713003",
        "PROCEDURE" => "71388002",
        _ => "71388002"
    };

    private static string MapCategoryToDisplay(string category) => category.ToUpperInvariant() switch
    {
        "LABORATORY" => "Laboratory procedure",
        "IMAGING" => "Imaging",
        "REFERRAL" => "Referral",
        "COUNSELING" => "Counseling",
        "EDUCATION" => "Education",
        "SURGICAL" or "SURGERY" => "Surgical procedure",
        "PROCEDURE" => "Procedure",
        _ => "Procedure"
    };

    private void SetPerformer(JsonObject node, ScenarioContext context)
    {
        var performerNode = new JsonObject();

        if (!string.IsNullOrEmpty(PerformerDisplay))
        {
            performerNode["display"] = PerformerDisplay;
        }
        else if (context.CurrentOrganization is not null)
        {
            performerNode["reference"] = $"Organization/{context.CurrentOrganization.Id}";
        }
        else if (context.CurrentPractitioner is not null)
        {
            performerNode["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}";
        }
        else
        {
            // Infer performer display from category
            var categoryValue = Category ?? InferCategory();
            performerNode["display"] = categoryValue.ToUpperInvariant() switch
            {
                "LABORATORY" => "Clinical Laboratory",
                "IMAGING" => "Radiology Department",
                "REFERRAL" => "Specialist Clinic",
                _ => "Healthcare Provider"
            };
        }

        node["performer"] = new JsonArray { performerNode };
    }

    private void SetReason(JsonObject node, ScenarioContext context)
    {
        if (ReasonCode is not null)
        {
            node["reasonCode"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = ReasonCode.System,
                            ["code"] = ReasonCode.Code,
                            ["display"] = ReasonCode.Display
                        }
                    }
                }
            };
        }
        else if (!string.IsNullOrEmpty(ReasonConditionAttribute) &&
                 context.HasAttribute(ReasonConditionAttribute))
        {
            var conditionId = context.GetAttribute<string>(ReasonConditionAttribute);
            node["reasonReference"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference"] = $"Condition/{conditionId}"
                }
            };
        }
        else if (!string.IsNullOrEmpty(ReasonDisplay))
        {
            node["reasonCode"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = ReasonDisplay
                }
            };
        }
    }

    #region Laboratory Order Factory Methods

    /// <summary>
    /// Creates a Complete Blood Count (CBC) with differential order.
    /// </summary>
    /// <remarks>
    /// CBC is a common laboratory test that measures red blood cells, white blood cells,
    /// hemoglobin, hematocrit, and platelets. The differential breaks down white cell types.
    /// LOINC code: 58410-2
    /// </remarks>
    public static ServiceRequestState CBCOrder() => new()
    {
        Name = "ServiceRequest_CBC",
        Code = ServiceRequestCodes.Laboratory.CBCWithDifferential,
        Category = "laboratory",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Comprehensive Metabolic Panel (CMP) order.
    /// </summary>
    /// <remarks>
    /// CMP includes glucose, electrolytes, kidney function tests, and liver enzymes.
    /// LOINC code: 24323-8
    /// </remarks>
    public static ServiceRequestState ComprehensiveMetabolicPanelOrder() => new()
    {
        Name = "ServiceRequest_CMP",
        Code = ServiceRequestCodes.Laboratory.ComprehensiveMetabolicPanel,
        Category = "laboratory",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Lipid Panel order.
    /// </summary>
    /// <remarks>
    /// Measures total cholesterol, HDL, LDL, and triglycerides.
    /// LOINC code: 57698-3
    /// </remarks>
    public static ServiceRequestState LipidPanelOrder() => new()
    {
        Name = "ServiceRequest_LipidPanel",
        Code = ServiceRequestCodes.Laboratory.LipidPanel,
        Category = "laboratory",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Hemoglobin A1c order for diabetes monitoring.
    /// </summary>
    /// <remarks>
    /// HbA1c reflects average blood glucose levels over the past 2-3 months.
    /// LOINC code: 4548-4
    /// </remarks>
    public static ServiceRequestState HemoglobinA1cOrder() => new()
    {
        Name = "ServiceRequest_HbA1c",
        Code = ServiceRequestCodes.Laboratory.HemoglobinA1c,
        Category = "laboratory",
        Priority = "routine",
        ReasonCode = FhirCode.Conditions.DiabetesType2
    };

    /// <summary>
    /// Creates a Thyroid Stimulating Hormone (TSH) order.
    /// </summary>
    /// <remarks>
    /// TSH is used to screen for and monitor thyroid disorders.
    /// LOINC code: 3016-3
    /// </remarks>
    public static ServiceRequestState TSHOrder() => new()
    {
        Name = "ServiceRequest_TSH",
        Code = ServiceRequestCodes.Laboratory.TSH,
        Category = "laboratory",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Prostate Specific Antigen (PSA) order.
    /// </summary>
    /// <remarks>
    /// PSA is used to screen for prostate cancer in men.
    /// LOINC code: 2857-1
    /// </remarks>
    public static ServiceRequestState PSAOrder() => new()
    {
        Name = "ServiceRequest_PSA",
        Code = ServiceRequestCodes.Laboratory.PSA,
        Category = "laboratory",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Urinalysis order.
    /// </summary>
    /// <remarks>
    /// Urinalysis tests for urinary tract infections, kidney disease, and diabetes.
    /// LOINC code: 24356-8
    /// </remarks>
    public static ServiceRequestState UrinalysisOrder() => new()
    {
        Name = "ServiceRequest_Urinalysis",
        Code = ServiceRequestCodes.Laboratory.Urinalysis,
        Category = "laboratory",
        Priority = "routine"
    };

    #endregion

    #region Imaging Order Factory Methods

    /// <summary>
    /// Creates a Chest X-ray order.
    /// </summary>
    /// <remarks>
    /// Used to evaluate lungs, heart, and chest wall.
    /// SNOMED CT code: 399208008
    /// </remarks>
    public static ServiceRequestState ChestXRayOrder() => new()
    {
        Name = "ServiceRequest_ChestXRay",
        Code = ServiceRequestCodes.ImagingStudies.ChestXRay,
        Category = "imaging",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a CT Chest order.
    /// </summary>
    /// <remarks>
    /// Detailed imaging of chest structures including lungs and mediastinum.
    /// SNOMED CT code: 241540006
    /// </remarks>
    public static ServiceRequestState CTChestOrder() => new()
    {
        Name = "ServiceRequest_CTChest",
        Code = ServiceRequestCodes.ImagingStudies.CTChest,
        Category = "imaging",
        Priority = "routine"
    };

    /// <summary>
    /// Creates an MRI Brain order.
    /// </summary>
    /// <remarks>
    /// Detailed brain imaging without radiation exposure.
    /// SNOMED CT code: 241684001
    /// </remarks>
    public static ServiceRequestState MRIBrainOrder() => new()
    {
        Name = "ServiceRequest_MRIBrain",
        Code = ServiceRequestCodes.ImagingStudies.MRIBrain,
        Category = "imaging",
        Priority = "routine"
    };

    /// <summary>
    /// Creates an Ultrasound Abdomen order.
    /// </summary>
    /// <remarks>
    /// Non-invasive imaging of abdominal organs.
    /// SNOMED CT code: 241490004
    /// </remarks>
    public static ServiceRequestState UltrasoundAbdomenOrder() => new()
    {
        Name = "ServiceRequest_USAbdomen",
        Code = ServiceRequestCodes.ImagingStudies.UltrasoundAbdomen,
        Category = "imaging",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Mammogram order.
    /// </summary>
    /// <remarks>
    /// Breast cancer screening imaging.
    /// SNOMED CT code: 71651007
    /// </remarks>
    public static ServiceRequestState MammogramOrder() => new()
    {
        Name = "ServiceRequest_Mammogram",
        Code = ServiceRequestCodes.ImagingStudies.Mammogram,
        Category = "imaging",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Bone Density Scan (DEXA) order.
    /// </summary>
    /// <remarks>
    /// Used to diagnose and monitor osteoporosis.
    /// SNOMED CT code: 312681000
    /// </remarks>
    public static ServiceRequestState BoneDensityScanOrder() => new()
    {
        Name = "ServiceRequest_DEXA",
        Code = ServiceRequestCodes.ImagingStudies.BoneDensityScan,
        Category = "imaging",
        Priority = "routine"
    };

    #endregion

    #region Specialist Referral Factory Methods

    /// <summary>
    /// Creates a Cardiology consultation referral.
    /// </summary>
    /// <remarks>
    /// Referral to a cardiologist for heart-related conditions.
    /// SNOMED CT code: 183524002
    /// </remarks>
    public static ServiceRequestState CardiologyReferral() => new()
    {
        Name = "ServiceRequest_CardiologyReferral",
        Code = ServiceRequestCodes.Referrals.CardiologyConsult,
        Category = "referral",
        Priority = "routine"
    };

    /// <summary>
    /// Creates an Orthopedic consultation referral.
    /// </summary>
    /// <remarks>
    /// Referral to an orthopedist for musculoskeletal conditions.
    /// SNOMED CT code: 183516009
    /// </remarks>
    public static ServiceRequestState OrthopedicReferral() => new()
    {
        Name = "ServiceRequest_OrthopedicReferral",
        Code = ServiceRequestCodes.Referrals.OrthopedicConsult,
        Category = "referral",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Psychiatry consultation referral.
    /// </summary>
    /// <remarks>
    /// Referral to a psychiatrist for mental health evaluation and treatment.
    /// SNOMED CT code: 183521005
    /// </remarks>
    public static ServiceRequestState PsychiatryReferral() => new()
    {
        Name = "ServiceRequest_PsychiatryReferral",
        Code = ServiceRequestCodes.Referrals.PsychiatryConsult,
        Category = "referral",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Physical Therapy referral.
    /// </summary>
    /// <remarks>
    /// Referral for physical rehabilitation and therapy services.
    /// SNOMED CT code: 183523008
    /// </remarks>
    public static ServiceRequestState PhysicalTherapyReferral() => new()
    {
        Name = "ServiceRequest_PTReferral",
        Code = ServiceRequestCodes.Referrals.PhysicalTherapy,
        Category = "referral",
        Priority = "routine"
    };

    /// <summary>
    /// Creates an Endocrinology consultation referral.
    /// </summary>
    /// <remarks>
    /// Referral to an endocrinologist for hormonal and metabolic disorders.
    /// SNOMED CT code: 183515008
    /// </remarks>
    public static ServiceRequestState EndocrinologyReferral() => new()
    {
        Name = "ServiceRequest_EndocrinologyReferral",
        Code = ServiceRequestCodes.Referrals.EndocrinologyConsult,
        Category = "referral",
        Priority = "routine"
    };

    /// <summary>
    /// Creates a Gastroenterology consultation referral.
    /// </summary>
    /// <remarks>
    /// Referral to a gastroenterologist for digestive system disorders.
    /// SNOMED CT code: 183522003
    /// </remarks>
    public static ServiceRequestState GastroenterologyReferral() => new()
    {
        Name = "ServiceRequest_GIReferral",
        Code = ServiceRequestCodes.Referrals.GastroenterologyConsult,
        Category = "referral",
        Priority = "routine"
    };

    #endregion

    #region Urgent/Stat Order Factory Methods

    /// <summary>
    /// Creates an urgent CBC order for acute conditions.
    /// </summary>
    public static ServiceRequestState UrgentCBCOrder() => new()
    {
        Name = "ServiceRequest_UrgentCBC",
        Code = ServiceRequestCodes.Laboratory.CBCWithDifferential,
        Category = "laboratory",
        Priority = "urgent"
    };

    /// <summary>
    /// Creates a stat Comprehensive Metabolic Panel order.
    /// </summary>
    public static ServiceRequestState StatMetabolicPanelOrder() => new()
    {
        Name = "ServiceRequest_StatCMP",
        Code = ServiceRequestCodes.Laboratory.ComprehensiveMetabolicPanel,
        Category = "laboratory",
        Priority = "stat"
    };

    /// <summary>
    /// Creates an urgent chest X-ray order.
    /// </summary>
    public static ServiceRequestState UrgentChestXRayOrder() => new()
    {
        Name = "ServiceRequest_UrgentChestXRay",
        Code = ServiceRequestCodes.ImagingStudies.ChestXRay,
        Category = "imaging",
        Priority = "urgent"
    };

    #endregion
}
