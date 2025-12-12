// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates an AllergyIntolerance resource.
/// AllergyIntolerance represents allergies and intolerances with reaction details.
/// </summary>
public sealed class AllergyIntoleranceState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the allergen code (from Allergens constants).
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the clinical status ("active", "inactive", "resolved").
    /// </summary>
    public string ClinicalStatus { get; init; } = "active";

    /// <summary>
    /// Gets or sets the verification status ("unconfirmed", "confirmed", "refuted", "entered-in-error").
    /// </summary>
    public string VerificationStatus { get; init; } = "confirmed";

    /// <summary>
    /// Gets or sets the type ("allergy", "intolerance").
    /// </summary>
    public string Type { get; init; } = "allergy";

    /// <summary>
    /// Gets or sets the category ("food", "medication", "environment", "biologic").
    /// If not specified, it is inferred from the allergen code.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets or sets the severity.
    /// </summary>
    public string Severity { get; init; } = AllergyIntoleranceSeverity.Moderate;

    /// <summary>
    /// Gets or sets the criticality ("low", "high", "unable-to-assess").
    /// If not specified, it is inferred from severity.
    /// </summary>
    public string? Criticality { get; init; }

    /// <summary>
    /// Gets or sets the list of reactions (manifestations like "Anaphylaxis", "Hives", "Rash").
    /// </summary>
    public IReadOnlyList<string>? Reactions { get; init; }

    /// <summary>
    /// Gets or sets the date when the allergy was recorded (uses current timeline if not specified).
    /// </summary>
    public DateTime? RecordedDate { get; init; }

    /// <summary>
    /// Gets or sets the onset date of the allergy.
    /// </summary>
    public DateTime? OnsetDate { get; init; }

    /// <summary>
    /// Gets or sets additional notes about the allergy.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Creates an AllergyIntolerance resource linked to the patient.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create AllergyIntolerance without a Patient. Ensure InitialState runs first.");
        }

        var allergy = faker.Generate("AllergyIntolerance");
        var node = allergy.MutableNode;

        // Remove any existing choice element variants to avoid conflicts
        // The faker may generate placeholder values for choice elements
        RemoveChoiceConflicts(node, "onset");

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();

        // Set clinical status (required in R4+, optional in STU3)
        // Always set it for completeness, but note the requirement difference
        node["clinicalStatus"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical",
                    ["code"] = ClinicalStatus,
                    ["display"] = MapClinicalStatusToDisplay(ClinicalStatus)
                }
            }
        };

        // Set verification status (required in R4+, optional in STU3)
        // Always set it for completeness, but note the requirement difference
        node["verificationStatus"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/allergyintolerance-verification",
                    ["code"] = VerificationStatus,
                    ["display"] = MapVerificationStatusToDisplay(VerificationStatus)
                }
            }
        };

        // Set type
        node["type"] = Type;

        // Set category (infer if not specified)
        var categoryValue = Category ?? InferCategory();
        node["category"] = new JsonArray { categoryValue };

        // Set criticality (infer from severity if not specified)
        var criticalityValue = Criticality ?? MapSeverityToCriticality(Severity);
        node["criticality"] = criticalityValue;

        // Set allergen code
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
        node["patient"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set encounter reference if available
        // STU3 doesn't have an encounter field for AllergyIntolerance, so check if it should be skipped
        var encounterField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "AllergyIntolerance",
            "encounter");

        if (context.CurrentEncounter is not null && !string.IsNullOrEmpty(encounterField))
        {
            node[encounterField] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }
        else
        {
            // Clear any faker-generated encounter reference (R4+ only, STU3 doesn't have encounter)
            node.Remove("encounter");
        }

        // Set onset date using version-appropriate field name (R4+ normative is "onsetDateTime")
        var onsetDateTime = OnsetDate ?? context.CurrentTime.AddYears(-_faker.Random.Int(1, 20));
        var onsetField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "AllergyIntolerance",
            "onsetDateTime");
        node[onsetField] = onsetDateTime.ToString("o");

        // Set recorded date (STU3 uses "assertedDate" instead of "recordedDate")
        var recordedDateTime = RecordedDate ?? context.CurrentTime;
        var recordedDateField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "AllergyIntolerance",
            "recordedDate");
        node[recordedDateField] = recordedDateTime.ToString("o");

        // Set recorder (who documented this allergy)
        var recorderNode = new JsonObject
        {
            ["display"] = _faker.Name.FullName() + ", MD"
        };

        // Add practitioner reference if available
        if (context.CurrentPractitioner is not null)
        {
            recorderNode["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}";
        }

        node["recorder"] = recorderNode;

        // Set reactions if provided
        if (Reactions is { Count: > 0 })
        {
            node["reaction"] = CreateReactions();
        }

        // Set note if provided
        if (!string.IsNullOrEmpty(Note))
        {
            node["note"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"] = Note,
                    ["time"] = recordedDateTime.ToString("o")
                }
            };
        }

        // Add to context
        context.AddAllergy(allergy, Code.Display);
    }

    private string InferCategory()
    {
        // Infer category from the allergen code display text
        var display = Code.Display;

        // Drug allergies
        if (display.Contains("penicillin", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("sulfonamide", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("aspirin", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("nsaid", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("non-steroidal", StringComparison.OrdinalIgnoreCase))
        {
            return "medication";
        }

        // Food allergies
        if (display.Contains("peanut", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("nut", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("fish", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("shellfish", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("wheat", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("egg", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("dairy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("milk", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("soy", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("soya", StringComparison.OrdinalIgnoreCase))
        {
            return "food";
        }

        // Environmental allergies
        if (display.Contains("pollen", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("mold", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("mould", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("dust", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("dander", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("cat", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("dog", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("animal", StringComparison.OrdinalIgnoreCase))
        {
            return "environment";
        }

        // Biologic (insect venom, latex)
        if (display.Contains("venom", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("bee", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("wasp", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("latex", StringComparison.OrdinalIgnoreCase))
        {
            return "biologic";
        }

        // Default to environment for unknown
        return "environment";
    }

    private static string MapSeverityToCriticality(string severity) => severity.ToUpperInvariant() switch
    {
        "SEVERE" => "high",
        "MODERATE" => "low",
        "MILD" => "low",
        _ => "unable-to-assess"
    };

    private static string MapClinicalStatusToDisplay(string status) => status.ToUpperInvariant() switch
    {
        "ACTIVE" => "Active",
        "INACTIVE" => "Inactive",
        "RESOLVED" => "Resolved",
        _ => status
    };

    private static string MapVerificationStatusToDisplay(string status) => status.ToUpperInvariant() switch
    {
        "UNCONFIRMED" => "Unconfirmed",
        "CONFIRMED" => "Confirmed",
        "REFUTED" => "Refuted",
        "ENTERED-IN-ERROR" => "Entered in Error",
        _ => status
    };

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private JsonArray CreateReactions()
    {
        var reactions = new JsonArray();

        foreach (var reaction in Reactions!)
        {
            var manifestationCode = MapReactionToCode(reaction);

            var reactionNode = new JsonObject
            {
                ["manifestation"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = FhirCode.Systems.SnomedCt,
                                ["code"] = manifestationCode.Code,
                                ["display"] = manifestationCode.Display
                            }
                        },
                        ["text"] = reaction
                    }
                },
                ["severity"] = Severity
            };

            // Note: AllergyIntolerance.reaction.onset[x] has onsetDateTime as the R4+ normative
            // For now we set as a simple string representation for cross-version compatibility
            reactionNode["onset"] = _faker.Date.Recent(365).ToString("o");
            reactions.Add(reactionNode);
        }

        return reactions;
    }

    private static (string Code, string Display) MapReactionToCode(string reaction)
    {
        var reactionUpper = reaction.ToUpperInvariant();

        return reactionUpper switch
        {
            "ANAPHYLAXIS" or "ANAPHYLACTIC SHOCK" => ("39579001", "Anaphylaxis"),
            "HIVES" or "URTICARIA" => ("126485001", "Urticaria"),
            "RASH" or "SKIN RASH" => ("271807003", "Eruption of skin"),
            "SWELLING" or "ANGIOEDEMA" => ("41291007", "Angioedema"),
            "ITCHING" or "PRURITUS" => ("418290006", "Itching"),
            "NAUSEA" => ("422587007", "Nausea"),
            "VOMITING" => ("422400008", "Vomiting"),
            "DIARRHEA" => ("62315008", "Diarrhea"),
            "WHEEZING" => ("56018004", "Wheezing"),
            "SHORTNESS OF BREATH" or "DYSPNEA" => ("267036007", "Dyspnea"),
            "COUGH" => ("49727002", "Coughing"),
            "RUNNY NOSE" or "RHINORRHEA" => ("64531003", "Rhinorrhea"),
            "SNEEZING" => ("162367006", "Sneezing"),
            "WATERY EYES" or "LACRIMATION" => ("418290006", "Excessive tearing"),
            "THROAT SWELLING" => ("102616008", "Swelling of throat"),
            "STOMACH CRAMPS" or "ABDOMINAL PAIN" => ("21522001", "Abdominal pain"),
            _ => ("271807003", reaction) // Default to skin eruption
        };
    }

    #region Factory Methods

    /// <summary>
    /// Creates a peanut allergy with severe anaphylaxis reaction.
    /// </summary>
    public static AllergyIntoleranceState PeanutAllergy() => new()
    {
        Code = Allergens.Peanuts,
        Severity = AllergyIntoleranceSeverity.Severe,
        Category = "food",
        Reactions = ["Anaphylaxis", "Hives", "Swelling"]
    };

    /// <summary>
    /// Creates a penicillin allergy with severe reaction.
    /// </summary>
    public static AllergyIntoleranceState PenicillinAllergy() => new()
    {
        Code = Allergens.Penicillin,
        Severity = AllergyIntoleranceSeverity.Severe,
        Category = "medication",
        Reactions = ["Anaphylaxis", "Rash", "Swelling"]
    };

    /// <summary>
    /// Creates a shellfish allergy.
    /// </summary>
    public static AllergyIntoleranceState ShellfishAllergy() => new()
    {
        Code = Allergens.Shellfish,
        Severity = AllergyIntoleranceSeverity.Moderate,
        Category = "food",
        Reactions = ["Hives", "Nausea", "Vomiting"]
    };

    /// <summary>
    /// Creates a latex allergy.
    /// </summary>
    public static AllergyIntoleranceState LatexAllergy() => new()
    {
        Code = Allergens.Latex,
        Severity = AllergyIntoleranceSeverity.Moderate,
        Category = "biologic",
        Reactions = ["Hives", "Itching", "Rash"]
    };

    /// <summary>
    /// Creates a seasonal grass pollen allergy (hay fever).
    /// </summary>
    public static AllergyIntoleranceState GrassPollenAllergy() => new()
    {
        Code = Allergens.GrassPollen,
        Severity = AllergyIntoleranceSeverity.Mild,
        Category = "environment",
        Reactions = ["Sneezing", "Runny nose", "Watery eyes"]
    };

    /// <summary>
    /// Creates a dust mite allergy.
    /// </summary>
    public static AllergyIntoleranceState DustMiteAllergy() => new()
    {
        Code = Allergens.DustMite,
        Severity = AllergyIntoleranceSeverity.Mild,
        Category = "environment",
        Reactions = ["Sneezing", "Cough", "Runny nose"]
    };

    /// <summary>
    /// Creates an aspirin/NSAID allergy.
    /// </summary>
    public static AllergyIntoleranceState AspirinAllergy() => new()
    {
        Code = Allergens.Aspirin,
        Severity = AllergyIntoleranceSeverity.Moderate,
        Category = "medication",
        Reactions = ["Hives", "Swelling", "Wheezing"]
    };

    /// <summary>
    /// Creates a sulfa drug allergy.
    /// </summary>
    public static AllergyIntoleranceState SulfonamideAllergy() => new()
    {
        Code = Allergens.Sulfonamides,
        Severity = AllergyIntoleranceSeverity.Severe,
        Category = "medication",
        Reactions = ["Rash", "Hives", "Anaphylaxis"]
    };

    /// <summary>
    /// Creates a bee venom allergy.
    /// </summary>
    public static AllergyIntoleranceState BeeVenomAllergy() => new()
    {
        Code = Allergens.BeeVenom,
        Severity = AllergyIntoleranceSeverity.Severe,
        Category = "biologic",
        Reactions = ["Anaphylaxis", "Swelling", "Shortness of breath"]
    };

    /// <summary>
    /// Creates a cat dander allergy.
    /// </summary>
    public static AllergyIntoleranceState CatAllergy() => new()
    {
        Code = Allergens.CatDander,
        Severity = AllergyIntoleranceSeverity.Mild,
        Category = "environment",
        Reactions = ["Sneezing", "Watery eyes", "Itching"]
    };

    /// <summary>
    /// Creates an egg allergy.
    /// </summary>
    public static AllergyIntoleranceState EggAllergy() => new()
    {
        Code = Allergens.Eggs,
        Severity = AllergyIntoleranceSeverity.Moderate,
        Category = "food",
        Reactions = ["Hives", "Nausea", "Stomach cramps"]
    };

    /// <summary>
    /// Creates a dairy/milk allergy.
    /// </summary>
    public static AllergyIntoleranceState DairyAllergy() => new()
    {
        Code = Allergens.Milk,
        Severity = AllergyIntoleranceSeverity.Moderate,
        Category = "food",
        Type = "intolerance",
        Reactions = ["Nausea", "Diarrhea", "Stomach cramps"]
    };

    #endregion
}
