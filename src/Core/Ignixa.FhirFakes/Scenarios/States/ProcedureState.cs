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
/// State that creates a Procedure resource.
/// Procedures represent surgeries, diagnostic procedures, and therapeutic procedures.
/// </summary>
public sealed class ProcedureState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the procedure code (from Procedures constants).
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the procedure status ("preparation", "in-progress", "not-done", "on-hold", "stopped", "completed", "entered-in-error", "unknown").
    /// </summary>
    public string Status { get; init; } = "completed";

    /// <summary>
    /// Gets or sets the duration of the procedure.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets or sets the outcome text (e.g., "Successful", "No complications").
    /// </summary>
    public string? Outcome { get; init; }

    /// <summary>
    /// Gets or sets the body site where the procedure was performed.
    /// </summary>
    public string? BodySite { get; init; }

    /// <summary>
    /// Gets or sets the reason for the procedure (human-readable text or condition name).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets or sets the reason code for the procedure.
    /// </summary>
    public FhirCode? ReasonCode { get; init; }

    /// <summary>
    /// Gets or sets the attribute name of the condition this procedure is treating.
    /// </summary>
    public string? ReasonConditionAttribute { get; init; }

    /// <summary>
    /// Gets or sets the category ("procedure", "surgery", "diagnostic", "therapeutic").
    /// If not specified, it is inferred from the procedure code.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets or sets the performer name.
    /// </summary>
    public string? PerformerName { get; init; }

    /// <summary>
    /// Gets or sets additional notes about the procedure.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Gets or sets the complication details if any occurred.
    /// </summary>
    public string? Complication { get; init; }

    /// <summary>
    /// Gets or sets the follow-up instructions.
    /// </summary>
    public string? FollowUp { get; init; }

    /// <summary>
    /// Creates a Procedure resource linked to the patient and encounter.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Procedure without a Patient. Ensure InitialState runs first.");
        }

        var procedure = faker.Generate("Procedure");
        var node = procedure.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;

        // Set category
        var categoryValue = Category ?? InferCategory();
        node["category"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://snomed.info/sct",
                    ["code"] = MapCategoryToCode(categoryValue),
                    ["display"] = MapCategoryToDisplay(categoryValue)
                }
            }
        };

        // Set procedure code
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

        // Set performed period
        var startTime = context.CurrentTime;
        var duration = Duration ?? InferDuration();
        var endTime = startTime.Add(duration);

        node["performedPeriod"] = new JsonObject
        {
            ["start"] = startTime.ToString("o"),
            ["end"] = endTime.ToString("o")
        };

        // Set performer
        var performerName = PerformerName ?? GeneratePerformerName();
        var performerActor = new JsonObject
        {
            ["display"] = performerName
        };

        // Add practitioner reference if available
        if (context.CurrentPractitioner is not null)
        {
            performerActor["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}";
        }

        node["performer"] = new JsonArray
        {
            new JsonObject
            {
                ["actor"] = performerActor,
                ["function"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = "http://snomed.info/sct",
                            ["code"] = "304292004",
                            ["display"] = "Surgeon"
                        }
                    }
                }
            }
        };

        // Set location (operating room, procedure room, etc.)
        node["location"] = new JsonObject
        {
            ["display"] = GenerateLocation()
        };

        // Set reason (code or reference)
        SetReason(node, context);

        // Set body site if provided
        if (!string.IsNullOrEmpty(BodySite))
        {
            node["bodySite"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = FhirCode.Systems.SnomedCt,
                            ["code"] = MapBodySiteToCode(BodySite),
                            ["display"] = BodySite
                        }
                    },
                    ["text"] = BodySite
                }
            };
        }

        // Set outcome if provided
        if (!string.IsNullOrEmpty(Outcome))
        {
            node["outcome"] = new JsonObject
            {
                ["text"] = Outcome
            };
        }

        // Set complication if provided
        if (!string.IsNullOrEmpty(Complication))
        {
            node["complication"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = Complication
                }
            };
        }

        // Set follow-up if provided
        if (!string.IsNullOrEmpty(FollowUp))
        {
            node["followUp"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = FollowUp
                }
            };
        }

        // Set note if provided
        if (!string.IsNullOrEmpty(Note))
        {
            node["note"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = Note,
                    ["time"] = endTime.ToString("o")
                }
            };
        }

        // Add to context
        context.AddProcedure(procedure, Code.Display);
    }

    private string InferCategory()
    {
        var display = Code.Display;

        // Surgical procedures
        if (display.Contains("ectomy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("otomy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("bypass", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("replacement", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("fusion", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("resection", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("incision", StringComparison.OrdinalIgnoreCase))
        {
            return "surgery";
        }

        // Diagnostic/Imaging procedures
        if (display.Contains("scopy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("graphy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("scan", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("imaging", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("x-ray", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("mri", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("ct ", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("ultrasound", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("biopsy", StringComparison.OrdinalIgnoreCase))
        {
            return "diagnostic";
        }

        // Therapeutic procedures
        if (display.Contains("transfusion", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("ventilation", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("catheter", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("lithotripsy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("cardioversion", StringComparison.OrdinalIgnoreCase))
        {
            return "therapeutic";
        }

        return "procedure";
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private TimeSpan InferDuration()
    {
        var display = Code.Display;

        // Major surgeries
        if (display.Contains("bypass", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("replacement", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("fusion", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("colectomy", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(_faker.Random.Int(2, 6));
        }

        // Moderate procedures
        if (display.Contains("ectomy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("catheterization", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(_faker.Random.Int(60, 180));
        }

        // Endoscopic procedures
        if (display.Contains("scopy", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(_faker.Random.Int(20, 60));
        }

        // Imaging procedures
        if (display.Contains("scan", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("imaging", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("x-ray", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("ultrasound", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(_faker.Random.Int(15, 45));
        }

        // Default
        return TimeSpan.FromMinutes(_faker.Random.Int(30, 90));
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GeneratePerformerName()
    {
        var specialties = new[]
        {
            "General Surgery", "Orthopedics", "Cardiology",
            "Gastroenterology", "Radiology", "Urology"
        };

        var specialty = specialties[_faker.Random.Int(0, specialties.Length - 1)];
        return $"Dr. {_faker.Name.LastName()}, {specialty}";
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GenerateLocation()
    {
        var category = Category ?? InferCategory();

        return category switch
        {
            "surgery" => $"Operating Room {_faker.Random.Int(1, 12)}",
            "diagnostic" => "Procedure Room" + (_faker.Random.Bool() ? $" {_faker.Random.Int(1, 5)}" : string.Empty),
            "therapeutic" => "Treatment Room",
            _ => "Procedure Room"
        };
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
        else if (!string.IsNullOrEmpty(Reason))
        {
            node["reasonCode"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = Reason
                }
            };
        }
    }

    private static string MapCategoryToCode(string category) => category.ToUpperInvariant() switch
    {
        "SURGERY" => "387713003",
        "DIAGNOSTIC" => "103693007",
        "THERAPEUTIC" => "277132007",
        _ => "71388002"
    };

    private static string MapCategoryToDisplay(string category) => category.ToUpperInvariant() switch
    {
        "SURGERY" => "Surgical procedure",
        "DIAGNOSTIC" => "Diagnostic procedure",
        "THERAPEUTIC" => "Therapeutic procedure",
        _ => "Procedure"
    };

    private static string MapBodySiteToCode(string bodySite)
    {
        var site = bodySite.ToUpperInvariant();

        return site switch
        {
            "APPENDIX" => "66754008",
            "GALLBLADDER" => "28231008",
            "COLON" or "ENTIRE COLON" => "71854001",
            "HEART" => "80891009",
            "KNEE" => "72696002",
            "HIP" => "24136001",
            "BRAIN" => "12738006",
            "SPINE" or "LUMBAR SPINE" => "122496007",
            "CHEST" => "51185008",
            "ABDOMEN" => "818983003",
            "HEAD" => "69536005",
            "LEFT ARM" => "368208006",
            "RIGHT ARM" => "368209003",
            "LEFT LEG" => "61396006",
            "RIGHT LEG" => "11207009",
            _ => "38866009" // Generic anatomical structure
        };
    }

    #region Factory Methods

    /// <summary>
    /// Creates an appendectomy procedure.
    /// </summary>
    public static ProcedureState Appendectomy() => new()
    {
        Code = Procedures.Appendectomy,
        Duration = TimeSpan.FromMinutes(90),
        BodySite = "Appendix",
        Category = "surgery",
        Outcome = "Appendix removed without complications",
        FollowUp = "Follow up in 2 weeks. Resume normal activities in 4-6 weeks."
    };

    /// <summary>
    /// Creates a colonoscopy procedure.
    /// </summary>
    public static ProcedureState Colonoscopy(string? outcome = null) => new()
    {
        Code = Procedures.Colonoscopy,
        Duration = TimeSpan.FromMinutes(45),
        BodySite = "Entire colon",
        Category = "diagnostic",
        Outcome = outcome ?? "Normal colonoscopy. No polyps identified.",
        FollowUp = "Repeat colonoscopy in 10 years if no findings."
    };

    /// <summary>
    /// Creates a cholecystectomy (gallbladder removal) procedure.
    /// </summary>
    public static ProcedureState Cholecystectomy() => new()
    {
        Code = Procedures.Cholecystectomy,
        Duration = TimeSpan.FromMinutes(120),
        BodySite = "Gallbladder",
        Category = "surgery",
        Outcome = "Laparoscopic cholecystectomy completed without complications",
        FollowUp = "Follow up in 2 weeks. Low-fat diet recommended initially."
    };

    /// <summary>
    /// Creates a coronary artery bypass graft (CABG) procedure.
    /// </summary>
    public static ProcedureState CABG(string? reason = null) => new()
    {
        Code = Procedures.CABG,
        Duration = TimeSpan.FromHours(4),
        BodySite = "Heart",
        Category = "surgery",
        Reason = reason ?? "Coronary artery disease",
        Outcome = "Triple vessel CABG completed successfully",
        FollowUp = "Cardiac rehabilitation program. Follow up in 6 weeks."
    };

    /// <summary>
    /// Creates a total knee replacement procedure.
    /// </summary>
    public static ProcedureState TotalKneeReplacement() => new()
    {
        Code = Procedures.TotalKneeReplacement,
        Duration = TimeSpan.FromHours(2),
        BodySite = "Knee",
        Category = "surgery",
        Outcome = "Total knee arthroplasty completed without complications",
        FollowUp = "Physical therapy to begin post-op day 1. Follow up in 6 weeks."
    };

    /// <summary>
    /// Creates a total hip replacement procedure.
    /// </summary>
    public static ProcedureState TotalHipReplacement() => new()
    {
        Code = Procedures.TotalHipReplacement,
        Duration = TimeSpan.FromHours(2),
        BodySite = "Hip",
        Category = "surgery",
        Outcome = "Total hip arthroplasty completed without complications",
        FollowUp = "Physical therapy to begin post-op day 1. Follow up in 6 weeks."
    };

    /// <summary>
    /// Creates an upper endoscopy (EGD) procedure.
    /// </summary>
    public static ProcedureState UpperEndoscopy(string? outcome = null) => new()
    {
        Code = Procedures.UpperEndoscopy,
        Duration = TimeSpan.FromMinutes(20),
        Category = "diagnostic",
        Outcome = outcome ?? "Normal esophagogastroduodenoscopy. No significant findings.",
        FollowUp = "Resume normal diet. Repeat if symptoms persist."
    };

    /// <summary>
    /// Creates a cardiac catheterization procedure.
    /// </summary>
    public static ProcedureState CardiacCatheterization(string? outcome = null) => new()
    {
        Code = Procedures.CardiacCatheterization,
        Duration = TimeSpan.FromMinutes(60),
        BodySite = "Heart",
        Category = "diagnostic",
        Outcome = outcome ?? "Coronary angiography completed. Results reviewed with patient.",
        FollowUp = "Keep access site clean and dry. Follow up in 1 week."
    };

    /// <summary>
    /// Creates a CT scan procedure.
    /// </summary>
    public static ProcedureState CTScan(string bodyPart = "Chest") => new()
    {
        Code = Procedures.CTScan,
        Duration = TimeSpan.FromMinutes(30),
        BodySite = bodyPart,
        Category = "diagnostic",
        Outcome = "CT scan completed. Radiology report to follow."
    };

    /// <summary>
    /// Creates an MRI scan procedure.
    /// </summary>
    public static ProcedureState MRIScan(string bodyPart = "Brain") => new()
    {
        Code = Procedures.MRIScan,
        Duration = TimeSpan.FromMinutes(45),
        BodySite = bodyPart,
        Category = "diagnostic",
        Outcome = "MRI completed. Radiology report to follow."
    };

    /// <summary>
    /// Creates a biopsy procedure.
    /// </summary>
    public static ProcedureState Biopsy(string site = "Skin") => new()
    {
        Code = Procedures.Biopsy,
        Duration = TimeSpan.FromMinutes(30),
        BodySite = site,
        Category = "diagnostic",
        Outcome = "Biopsy specimen obtained. Pathology results pending.",
        FollowUp = "Pathology results expected in 3-5 days. Follow up scheduled."
    };

    /// <summary>
    /// Creates a cesarean section procedure.
    /// </summary>
    public static ProcedureState CesareanSection() => new()
    {
        Code = Procedures.CesareanSection,
        Duration = TimeSpan.FromMinutes(60),
        BodySite = "Abdomen",
        Category = "surgery",
        Outcome = "Cesarean delivery completed. Baby delivered without complications.",
        FollowUp = "Follow up in 2 weeks for incision check."
    };

    #endregion
}
