// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Represents a planned activity within a care plan.
/// Defines specific interventions, treatments, or actions to achieve care plan goals.
/// </summary>
/// <param name="Status">Activity status: not-started, scheduled, in-progress, on-hold, completed, cancelled, stopped, unknown, entered-in-error.</param>
/// <param name="Detail">The activity code (SNOMED CT) describing the intervention.</param>
/// <param name="Description">Human-readable description of the activity.</param>
/// <param name="ScheduledStart">When the activity should start.</param>
/// <param name="ScheduledEnd">When the activity should end.</param>
public record CarePlanActivity(
    string Status,
    FhirCode Detail,
    string? Description = null,
    DateTime? ScheduledStart = null,
    DateTime? ScheduledEnd = null);

/// <summary>
/// State that creates a CarePlan resource representing a coordinated care plan.
/// CarePlans define activities to achieve goals and coordinate care across providers.
/// Supports US Core compliance for care coordination scenarios.
/// </summary>
public sealed class CarePlanState : ScenarioState
{
    /// <summary>
    /// Gets or sets the human-readable title of the care plan.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets or sets the status of the care plan.
    /// Valid values: draft, active, on-hold, revoked, completed, entered-in-error, unknown.
    /// </summary>
    public string Status { get; init; } = "active";

    /// <summary>
    /// Gets or sets the intent of the care plan.
    /// Valid values: proposal, plan, order, option.
    /// </summary>
    public string Intent { get; init; } = "plan";

    /// <summary>
    /// Gets or sets the categories of the care plan.
    /// Common categories: assess-plan, longitudinal, episode, careteam.
    /// </summary>
    public IReadOnlyList<FhirCode>? Categories { get; init; }

    /// <summary>
    /// Gets or sets the IDs of Goal resources that this care plan addresses.
    /// </summary>
    public IReadOnlyList<string>? GoalIds { get; init; }

    /// <summary>
    /// Gets or sets the planned activities in this care plan.
    /// </summary>
    public IReadOnlyList<CarePlanActivity>? Activities { get; init; }

    /// <summary>
    /// Gets or sets the start date of the care plan period.
    /// </summary>
    public DateTime? PeriodStart { get; init; }

    /// <summary>
    /// Gets or sets the end date of the care plan period.
    /// </summary>
    public DateTime? PeriodEnd { get; init; }

    /// <summary>
    /// Gets or sets the description/summary of the care plan.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets additional notes about the care plan.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Gets or sets the attribute name for storing this care plan's ID for later reference.
    /// </summary>
    public string? AssignToAttribute { get; init; }

    /// <summary>
    /// Gets or sets the related condition attribute name.
    /// If set, the care plan will reference the condition stored in this attribute.
    /// </summary>
    public string? RelatedConditionAttribute { get; init; }

    /// <summary>
    /// Creates a CarePlan resource linked to the patient and optionally to goals and conditions.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create CarePlan without a Patient. Ensure InitialState runs first.");
        }

        var carePlan = faker.Generate("CarePlan");
        var node = carePlan.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;
        node["intent"] = Intent;

        // Set title
        node["title"] = Title;

        // Set description if provided
        if (!string.IsNullOrEmpty(Description))
        {
            node["description"] = Description;
        }

        // Set category (US Core requires at least one category)
        var categoryArray = new JsonArray();
        if (Categories is { Count: > 0 })
        {
            foreach (var category in Categories)
            {
                categoryArray.Add(new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = category.System,
                            ["code"] = category.Code,
                            ["display"] = category.Display
                        }
                    }
                });
            }
        }
        else
        {
            // Default to assess-plan category (US Core)
            categoryArray.Add(new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://hl7.org/fhir/us/core/CodeSystem/careplan-category",
                        ["code"] = "assess-plan",
                        ["display"] = "Assessment and Plan of Treatment"
                    }
                }
            });
        }
        node["category"] = categoryArray;

        // Set subject reference (patient)
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

        // Set period
        var periodStart = PeriodStart ?? context.CurrentTime;
        node["period"] = new JsonObject
        {
            ["start"] = periodStart.ToString("yyyy-MM-dd")
        };
        if (PeriodEnd.HasValue)
        {
            node["period"]!.AsObject()["end"] = PeriodEnd.Value.ToString("yyyy-MM-dd");
        }

        // Set created date
        node["created"] = context.CurrentTime.ToString("o");

        // Set author if practitioner available
        if (context.CurrentPractitioner is not null)
        {
            node["author"] = new JsonObject
            {
                ["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}"
            };
        }

        // Set goals
        if (GoalIds is { Count: > 0 })
        {
            var goalArray = new JsonArray();
            foreach (var goalId in GoalIds)
            {
                goalArray.Add(new JsonObject
                {
                    ["reference"] = $"Goal/{goalId}"
                });
            }
            node["goal"] = goalArray;
        }
        else if (context.Goals.Count > 0)
        {
            // Auto-reference all goals in context
            var goalArray = new JsonArray();
            foreach (var goal in context.Goals)
            {
                goalArray.Add(new JsonObject
                {
                    ["reference"] = $"Goal/{goal.Id}"
                });
            }
            node["goal"] = goalArray;
        }

        // Set addresses (related conditions)
        if (!string.IsNullOrEmpty(RelatedConditionAttribute) &&
            context.HasAttribute(RelatedConditionAttribute))
        {
            var conditionId = context.GetAttribute<string>(RelatedConditionAttribute);
            node["addresses"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference"] = $"Condition/{conditionId}"
                }
            };
        }

        // Set activities
        if (Activities is { Count: > 0 })
        {
            var activityArray = new JsonArray();
            foreach (var activity in Activities)
            {
                var activityNode = new JsonObject
                {
                    ["detail"] = new JsonObject
                    {
                        ["status"] = activity.Status,
                        ["code"] = new JsonObject
                        {
                            ["coding"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["system"] = activity.Detail.System,
                                    ["code"] = activity.Detail.Code,
                                    ["display"] = activity.Detail.Display
                                }
                            },
                            ["text"] = activity.Description ?? activity.Detail.Display
                        }
                    }
                };

                // Add description if provided
                if (!string.IsNullOrEmpty(activity.Description))
                {
                    activityNode["detail"]!.AsObject()["description"] = activity.Description;
                }

                // Add scheduled timing if provided
                if (activity.ScheduledStart.HasValue || activity.ScheduledEnd.HasValue)
                {
                    var scheduledPeriod = new JsonObject();
                    if (activity.ScheduledStart.HasValue)
                    {
                        scheduledPeriod["start"] = activity.ScheduledStart.Value.ToString("yyyy-MM-dd");
                    }
                    if (activity.ScheduledEnd.HasValue)
                    {
                        scheduledPeriod["end"] = activity.ScheduledEnd.Value.ToString("yyyy-MM-dd");
                    }
                    activityNode["detail"]!.AsObject()["scheduledPeriod"] = scheduledPeriod;
                }

                activityArray.Add(activityNode);
            }
            node["activity"] = activityArray;
        }

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
        context.AddCarePlan(carePlan, Title);

        // Store in attribute if requested
        if (!string.IsNullOrEmpty(AssignToAttribute))
        {
            context.SetAttribute(AssignToAttribute, carePlan.Id);
        }
    }

    #region CarePlan Category Codes

    /// <summary>
    /// US Core and FHIR care plan category codes.
    /// </summary>
    public static class CategoryCodes
    {
        /// <summary>Assessment and plan of treatment (US Core)</summary>
        public static readonly FhirCode AssessPlan = new(
            "http://hl7.org/fhir/us/core/CodeSystem/careplan-category",
            "assess-plan",
            "Assessment and Plan of Treatment");

        /// <summary>Longitudinal care-coordination focused care plan (SNOMED CT: 736353004)</summary>
        public static readonly FhirCode Longitudinal = new(
            FhirCode.Systems.SnomedCt,
            "736353004",
            "Longitudinal care plan");

        /// <summary>Episode of care focused care plan (SNOMED CT: 736354005)</summary>
        public static readonly FhirCode Episode = new(
            FhirCode.Systems.SnomedCt,
            "736354005",
            "Episode of care plan");

        /// <summary>Care team focused care plan (SNOMED CT: 734163000)</summary>
        public static readonly FhirCode CareTeam = new(
            FhirCode.Systems.SnomedCt,
            "734163000",
            "Care team");
    }

    #endregion

    #region Activity Codes (SNOMED CT)

    /// <summary>
    /// Common care plan activity codes (SNOMED CT).
    /// </summary>
    public static class ActivityCodes
    {
        /// <summary>Self blood glucose monitoring (SNOMED CT: 33747003)</summary>
        public static readonly FhirCode BloodGlucoseMonitoring = new(
            FhirCode.Systems.SnomedCt,
            "33747003",
            "Self-monitoring of blood glucose");

        /// <summary>Diet counseling (SNOMED CT: 410270001)</summary>
        public static readonly FhirCode DietCounseling = new(
            FhirCode.Systems.SnomedCt,
            "410270001",
            "Dietary counseling");

        /// <summary>Medication review (SNOMED CT: 182777000)</summary>
        public static readonly FhirCode MedicationReview = new(
            FhirCode.Systems.SnomedCt,
            "182777000",
            "Medication review");

        /// <summary>Physical therapy (SNOMED CT: 91251008)</summary>
        public static readonly FhirCode PhysicalTherapy = new(
            FhirCode.Systems.SnomedCt,
            "91251008",
            "Physical therapy");

        /// <summary>Smoking cessation counseling (SNOMED CT: 225323000)</summary>
        public static readonly FhirCode SmokingCessationCounseling = new(
            FhirCode.Systems.SnomedCt,
            "225323000",
            "Smoking cessation advice");

        /// <summary>Exercise counseling (SNOMED CT: 281090004)</summary>
        public static readonly FhirCode ExerciseCounseling = new(
            FhirCode.Systems.SnomedCt,
            "281090004",
            "Exercise therapy");

        /// <summary>Blood pressure monitoring (SNOMED CT: 46973005)</summary>
        public static readonly FhirCode BloodPressureMonitoring = new(
            FhirCode.Systems.SnomedCt,
            "46973005",
            "Blood pressure monitoring");

        /// <summary>Weight monitoring (SNOMED CT: 276885007)</summary>
        public static readonly FhirCode WeightMonitoring = new(
            FhirCode.Systems.SnomedCt,
            "276885007",
            "Core body weight measurement");

        /// <summary>Cardiac rehabilitation (SNOMED CT: 232026005)</summary>
        public static readonly FhirCode CardiacRehabilitation = new(
            FhirCode.Systems.SnomedCt,
            "232026005",
            "Cardiac rehabilitation");

        /// <summary>Wound care (SNOMED CT: 225358003)</summary>
        public static readonly FhirCode WoundCare = new(
            FhirCode.Systems.SnomedCt,
            "225358003",
            "Wound care");

        /// <summary>Pain management (SNOMED CT: 278414003)</summary>
        public static readonly FhirCode PainManagement = new(
            FhirCode.Systems.SnomedCt,
            "278414003",
            "Pain management");

        /// <summary>Psychological support (SNOMED CT: 76746007)</summary>
        public static readonly FhirCode PsychologicalSupport = new(
            FhirCode.Systems.SnomedCt,
            "76746007",
            "Psychological support");

        /// <summary>Follow-up visit (SNOMED CT: 390906007)</summary>
        public static readonly FhirCode FollowUpVisit = new(
            FhirCode.Systems.SnomedCt,
            "390906007",
            "Follow-up encounter");

        /// <summary>Lab work (SNOMED CT: 15220000)</summary>
        public static readonly FhirCode LabWork = new(
            FhirCode.Systems.SnomedCt,
            "15220000",
            "Laboratory test");

        /// <summary>Education/patient teaching (SNOMED CT: 409073007)</summary>
        public static readonly FhirCode PatientEducation = new(
            FhirCode.Systems.SnomedCt,
            "409073007",
            "Education");
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a diabetes management care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for diabetes management.</returns>
    public static CarePlanState DiabetesManagementPlan() => new()
    {
        Title = "Diabetes Management Plan",
        Description = "Comprehensive care plan for Type 2 Diabetes management including blood glucose control, diet, and exercise.",
        Categories = [CategoryCodes.AssessPlan, CategoryCodes.Longitudinal],
        Activities =
        [
            new("scheduled", ActivityCodes.BloodGlucoseMonitoring, "Check blood sugar daily"),
            new("scheduled", ActivityCodes.DietCounseling, "Meet with dietitian monthly"),
            new("scheduled", ActivityCodes.MedicationReview, "Review medications quarterly"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "150 minutes moderate exercise weekly"),
            new("scheduled", ActivityCodes.LabWork, "HbA1c every 3 months")
        ],
        PeriodEnd = DateTime.UtcNow.AddYears(1),
        Note = "Monitor for complications: retinopathy, nephropathy, neuropathy"
    };

    /// <summary>
    /// Creates a hypertension management care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for hypertension management.</returns>
    public static CarePlanState HypertensionManagementPlan() => new()
    {
        Title = "Hypertension Management Plan",
        Description = "Blood pressure control through medication, lifestyle modifications, and regular monitoring.",
        Categories = [CategoryCodes.AssessPlan],
        Activities =
        [
            new("scheduled", ActivityCodes.BloodPressureMonitoring, "Daily home blood pressure monitoring"),
            new("scheduled", ActivityCodes.MedicationReview, "Review antihypertensive medications"),
            new("scheduled", ActivityCodes.DietCounseling, "Low sodium DASH diet education"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "Regular aerobic exercise program"),
            new("scheduled", ActivityCodes.FollowUpVisit, "Follow-up visit in 2 weeks")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(6),
        Note = "Goal: Maintain BP < 130/80 mmHg"
    };

    /// <summary>
    /// Creates a cardiac rehabilitation care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for cardiac rehabilitation.</returns>
    public static CarePlanState CardiacRehabilitationPlan() => new()
    {
        Title = "Cardiac Rehabilitation Plan",
        Description = "Post-cardiac event rehabilitation program focusing on exercise, education, and lifestyle modification.",
        Categories = [CategoryCodes.AssessPlan, CategoryCodes.Episode],
        Activities =
        [
            new("scheduled", ActivityCodes.CardiacRehabilitation, "Phase II cardiac rehab 3x/week"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "Supervised exercise training"),
            new("scheduled", ActivityCodes.PatientEducation, "Cardiac health education sessions"),
            new("scheduled", ActivityCodes.PsychologicalSupport, "Psychological support for recovery"),
            new("scheduled", ActivityCodes.MedicationReview, "Optimize cardiac medications"),
            new("scheduled", ActivityCodes.DietCounseling, "Heart-healthy diet counseling")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(3),
        Note = "12-week structured cardiac rehabilitation program"
    };

    /// <summary>
    /// Creates a weight loss care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for weight management.</returns>
    public static CarePlanState WeightLossPlan() => new()
    {
        Title = "Weight Management Plan",
        Description = "Structured weight loss program through diet, exercise, and behavioral modification.",
        Categories = [CategoryCodes.AssessPlan],
        Activities =
        [
            new("scheduled", ActivityCodes.WeightMonitoring, "Weekly weight monitoring"),
            new("scheduled", ActivityCodes.DietCounseling, "Weekly nutrition counseling"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "Progressive exercise program"),
            new("scheduled", ActivityCodes.PatientEducation, "Behavioral modification support"),
            new("scheduled", ActivityCodes.FollowUpVisit, "Monthly progress review")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(6),
        Note = "Target: 1-2 lbs weight loss per week"
    };

    /// <summary>
    /// Creates a chronic pain management care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for chronic pain management.</returns>
    public static CarePlanState ChronicPainManagementPlan() => new()
    {
        Title = "Chronic Pain Management Plan",
        Description = "Multimodal approach to chronic pain management including medication, therapy, and lifestyle modifications.",
        Categories = [CategoryCodes.AssessPlan, CategoryCodes.Longitudinal],
        Activities =
        [
            new("scheduled", ActivityCodes.PainManagement, "Pain assessment and medication adjustment"),
            new("scheduled", ActivityCodes.PhysicalTherapy, "Physical therapy 2x/week"),
            new("scheduled", ActivityCodes.PsychologicalSupport, "Cognitive behavioral therapy for pain"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "Gentle exercise and stretching program"),
            new("scheduled", ActivityCodes.MedicationReview, "Monthly medication review")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(6),
        Note = "Focus on functional improvement and quality of life"
    };

    /// <summary>
    /// Creates a post-surgical care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for post-operative recovery.</returns>
    public static CarePlanState PostSurgicalCarePlan() => new()
    {
        Title = "Post-Surgical Recovery Plan",
        Description = "Comprehensive post-operative care plan including wound care, pain management, and rehabilitation.",
        Categories = [CategoryCodes.AssessPlan, CategoryCodes.Episode],
        Activities =
        [
            new("in-progress", ActivityCodes.WoundCare, "Daily wound assessment and care"),
            new("scheduled", ActivityCodes.PainManagement, "Post-operative pain management"),
            new("scheduled", ActivityCodes.PhysicalTherapy, "Mobility and rehabilitation exercises"),
            new("scheduled", ActivityCodes.MedicationReview, "Post-op medication management"),
            new("scheduled", ActivityCodes.FollowUpVisit, "Post-op follow-up in 2 weeks")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(2),
        Note = "Monitor for signs of infection or complications"
    };

    /// <summary>
    /// Creates a smoking cessation care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for smoking cessation.</returns>
    public static CarePlanState SmokingCessationPlan() => new()
    {
        Title = "Smoking Cessation Plan",
        Description = "Structured smoking cessation program with counseling and pharmacotherapy support.",
        Categories = [CategoryCodes.AssessPlan],
        Activities =
        [
            new("scheduled", ActivityCodes.SmokingCessationCounseling, "Weekly counseling sessions"),
            new("scheduled", ActivityCodes.MedicationReview, "Nicotine replacement/medication support"),
            new("scheduled", ActivityCodes.PsychologicalSupport, "Behavioral support and coping strategies"),
            new("scheduled", ActivityCodes.PatientEducation, "Education on smoking health effects"),
            new("scheduled", ActivityCodes.FollowUpVisit, "Regular follow-up monitoring")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(3),
        Note = "Set quit date and develop personalized quit plan"
    };

    /// <summary>
    /// Creates a mental health care plan.
    /// </summary>
    /// <returns>A configured CarePlanState for mental health management.</returns>
    public static CarePlanState MentalHealthCarePlan() => new()
    {
        Title = "Mental Health Care Plan",
        Description = "Integrated care plan for mental health management including therapy, medication, and support.",
        Categories = [CategoryCodes.AssessPlan, CategoryCodes.Longitudinal],
        Activities =
        [
            new("scheduled", ActivityCodes.PsychologicalSupport, "Weekly therapy sessions"),
            new("scheduled", ActivityCodes.MedicationReview, "Psychiatric medication management"),
            new("scheduled", ActivityCodes.PatientEducation, "Coping skills and self-care education"),
            new("scheduled", ActivityCodes.ExerciseCounseling, "Physical activity for mental wellness"),
            new("scheduled", ActivityCodes.FollowUpVisit, "Monthly psychiatric follow-up")
        ],
        PeriodEnd = DateTime.UtcNow.AddMonths(6),
        Note = "Focus on symptom management and functional improvement"
    };

    #endregion
}
