// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Represents a measurable target within a goal.
/// Used to define specific quantitative objectives like "HbA1c &lt; 7%" or "Weight loss 10 lbs".
/// </summary>
/// <param name="Measure">The LOINC code for the measurement (e.g., HbA1c, body weight).</param>
/// <param name="TargetValue">The target value to achieve (e.g., 7.0, 10).</param>
/// <param name="Comparator">The comparison operator (&lt;, &lt;=, &gt;, &gt;=, =).</param>
/// <param name="Unit">The unit of measure (e.g., %, mg/dL, lbs).</param>
public record GoalTarget(
    FhirCode Measure,
    decimal? TargetValue,
    string? Comparator,
    string? Unit);

/// <summary>
/// State that creates a Goal resource representing a desired health outcome.
/// Goals define measurable objectives that care plans and interventions aim to achieve.
/// Supports US Core compliance for care coordination scenarios.
/// </summary>
public sealed class GoalState : ScenarioState
{
    /// <summary>
    /// Gets or sets the goal description code (SNOMED CT).
    /// Describes what the goal is trying to achieve.
    /// </summary>
    public required FhirCode Description { get; init; }

    /// <summary>
    /// Gets or sets the lifecycle status of the goal.
    /// Valid values: proposed, planned, accepted, active, on-hold, completed, cancelled, entered-in-error, rejected.
    /// </summary>
    public string LifecycleStatus { get; init; } = "active";

    /// <summary>
    /// Gets or sets the achievement status of the goal.
    /// Valid values: in-progress, improving, worsening, no-change, achieved, sustaining, not-achieved, no-progress, not-attainable.
    /// </summary>
    public string? AchievementStatus { get; init; }

    /// <summary>
    /// Gets or sets the priority of the goal.
    /// Valid values: high-priority, medium-priority, low-priority.
    /// </summary>
    public string Priority { get; init; } = "medium-priority";

    /// <summary>
    /// Gets or sets the start date for pursuing the goal.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// Gets or sets the target date by which the goal should be achieved.
    /// </summary>
    public DateTime? TargetDate { get; init; }

    /// <summary>
    /// Gets or sets the measurable targets for this goal.
    /// Each target specifies a quantitative objective with measure, value, and comparator.
    /// </summary>
    public IReadOnlyList<GoalTarget>? Targets { get; init; }

    /// <summary>
    /// Gets or sets additional notes about the goal.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Gets or sets the attribute name for storing this goal's ID for later reference.
    /// </summary>
    public string? AssignToAttribute { get; init; }

    /// <summary>
    /// Gets or sets the related condition attribute name.
    /// If set, the goal will reference the condition stored in this attribute.
    /// </summary>
    public string? RelatedConditionAttribute { get; init; }

    /// <summary>
    /// Creates a Goal resource linked to the patient.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Goal without a Patient. Ensure InitialState runs first.");
        }

        var goal = faker.Generate("Goal");
        var node = goal.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["lifecycleStatus"] = LifecycleStatus;

        // Set achievement status if provided
        if (!string.IsNullOrEmpty(AchievementStatus))
        {
            node["achievementStatus"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/goal-achievement",
                        ["code"] = AchievementStatus,
                        ["display"] = FormatAchievementStatusDisplay(AchievementStatus)
                    }
                }
            };
        }

        // Set priority
        node["priority"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/goal-priority",
                    ["code"] = Priority,
                    ["display"] = FormatPriorityDisplay(Priority)
                }
            }
        };

        // Set description (required)
        node["description"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = Description.System,
                    ["code"] = Description.Code,
                    ["display"] = Description.Display
                }
            },
            ["text"] = Description.Display
        };

        // Set subject reference (patient)
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set start date
        var startDateValue = StartDate ?? context.CurrentTime;
        node["startDate"] = startDateValue.ToString("yyyy-MM-dd");

        // Set target
        if (Targets is { Count: > 0 } || TargetDate.HasValue)
        {
            var targetArray = new JsonArray();

            if (Targets is { Count: > 0 })
            {
                foreach (var target in Targets)
                {
                    var targetNode = new JsonObject
                    {
                        ["measure"] = new JsonObject
                        {
                            ["coding"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["system"] = target.Measure.System,
                                    ["code"] = target.Measure.Code,
                                    ["display"] = target.Measure.Display
                                }
                            }
                        }
                    };

                    if (target.TargetValue.HasValue)
                    {
                        targetNode["detailQuantity"] = new JsonObject
                        {
                            ["value"] = target.TargetValue.Value,
                            ["unit"] = target.Unit ?? string.Empty,
                            ["system"] = FhirCode.Systems.Ucum,
                            ["code"] = target.Unit ?? string.Empty,
                            ["comparator"] = target.Comparator ?? "<"
                        };
                    }

                    if (TargetDate.HasValue)
                    {
                        targetNode["dueDate"] = TargetDate.Value.ToString("yyyy-MM-dd");
                    }

                    targetArray.Add(targetNode);
                }
            }
            else if (TargetDate.HasValue)
            {
                // Target date only, no specific measure
                targetArray.Add(new JsonObject
                {
                    ["dueDate"] = TargetDate.Value.ToString("yyyy-MM-dd")
                });
            }

            node["target"] = targetArray;
        }

        // Set category (US Core requires at least one category)
        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/goal-category",
                        ["code"] = "physiotherapy",
                        ["display"] = "Physiotherapy"
                    }
                }
            }
        };

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

        // Set addresses (related conditions) if provided
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

        // Add to context
        context.AddGoal(goal, Description.Display);

        // Store in attribute if requested
        if (!string.IsNullOrEmpty(AssignToAttribute))
        {
            context.SetAttribute(AssignToAttribute, goal.Id);
        }
    }

    private static string FormatAchievementStatusDisplay(string code) => code switch
    {
        "in-progress" => "In Progress",
        "improving" => "Improving",
        "worsening" => "Worsening",
        "no-change" => "No Change",
        "achieved" => "Achieved",
        "sustaining" => "Sustaining",
        "not-achieved" => "Not Achieved",
        "no-progress" => "No Progress",
        "not-attainable" => "Not Attainable",
        _ => code
    };

    private static string FormatPriorityDisplay(string code) => code switch
    {
        "high-priority" => "High Priority",
        "medium-priority" => "Medium Priority",
        "low-priority" => "Low Priority",
        _ => code
    };

    #region Goal Description Codes (SNOMED CT)

    /// <summary>
    /// Common goal description codes (SNOMED CT).
    /// </summary>
    public static class GoalCodes
    {
        /// <summary>Weight loss goal (SNOMED CT: 289169006)</summary>
        public static readonly FhirCode WeightLoss = new(FhirCode.Systems.SnomedCt, "289169006", "Weight loss");

        /// <summary>Blood pressure control goal (SNOMED CT: 703423002)</summary>
        public static readonly FhirCode BloodPressureControl = new(FhirCode.Systems.SnomedCt, "703423002", "Blood pressure management");

        /// <summary>Glucose control goal (SNOMED CT: 698360004)</summary>
        public static readonly FhirCode GlucoseControl = new(FhirCode.Systems.SnomedCt, "698360004", "Glucose level control");

        /// <summary>Smoking cessation goal (SNOMED CT: 160617001)</summary>
        public static readonly FhirCode SmokingCessation = new(FhirCode.Systems.SnomedCt, "160617001", "Stopped smoking");

        /// <summary>Pain reduction goal (SNOMED CT: 225444004)</summary>
        public static readonly FhirCode PainReduction = new(FhirCode.Systems.SnomedCt, "225444004", "Pain score assessment");

        /// <summary>Increased physical activity goal (SNOMED CT: 226029004)</summary>
        public static readonly FhirCode IncreasedPhysicalActivity = new(FhirCode.Systems.SnomedCt, "226029004", "Physical activity");

        /// <summary>Medication adherence goal (SNOMED CT: 418284009)</summary>
        public static readonly FhirCode MedicationAdherence = new(FhirCode.Systems.SnomedCt, "418284009", "Medication compliance");

        /// <summary>Improved mobility goal (SNOMED CT: 249868004)</summary>
        public static readonly FhirCode ImprovedMobility = new(FhirCode.Systems.SnomedCt, "249868004", "Mobility");

        /// <summary>Reduced alcohol consumption goal (SNOMED CT: 228377005)</summary>
        public static readonly FhirCode ReducedAlcohol = new(FhirCode.Systems.SnomedCt, "228377005", "Alcohol intake");

        /// <summary>Improved diet/nutrition goal (SNOMED CT: 289141003)</summary>
        public static readonly FhirCode ImprovedNutrition = new(FhirCode.Systems.SnomedCt, "289141003", "Eating a healthy diet");

        /// <summary>Improved sleep goal (SNOMED CT: 248254009)</summary>
        public static readonly FhirCode ImprovedSleep = new(FhirCode.Systems.SnomedCt, "248254009", "Sleep pattern");

        /// <summary>Anxiety reduction goal (SNOMED CT: 48694002)</summary>
        public static readonly FhirCode AnxietyReduction = new(FhirCode.Systems.SnomedCt, "48694002", "Anxiety");

        /// <summary>Depression management goal (SNOMED CT: 35489007)</summary>
        public static readonly FhirCode DepressionManagement = new(FhirCode.Systems.SnomedCt, "35489007", "Depressive disorder");
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a weight loss goal with target pounds.
    /// </summary>
    /// <param name="pounds">Target weight loss in pounds.</param>
    /// <returns>A configured GoalState for weight loss.</returns>
    public static GoalState WeightLossGoal(decimal pounds) => new()
    {
        Description = GoalCodes.WeightLoss,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        Targets =
        [
            new GoalTarget(
                FhirCode.Observations.BodyWeight,
                pounds,
                "<",
                "[lb_av]")
        ],
        TargetDate = DateTime.UtcNow.AddMonths(6),
        Note = $"Target: Lose {pounds} pounds through diet and exercise"
    };

    /// <summary>
    /// Creates a blood pressure control goal with target systolic pressure.
    /// </summary>
    /// <param name="systolic">Target maximum systolic blood pressure in mmHg.</param>
    /// <returns>A configured GoalState for blood pressure control.</returns>
    public static GoalState BloodPressureControlGoal(int systolic = 130) => new()
    {
        Description = GoalCodes.BloodPressureControl,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        Targets =
        [
            new GoalTarget(
                FhirCode.Observations.BloodPressureSystolic,
                systolic,
                "<",
                "mm[Hg]")
        ],
        TargetDate = DateTime.UtcNow.AddMonths(3),
        Note = $"Target: Maintain systolic BP < {systolic} mmHg"
    };

    /// <summary>
    /// Creates a glucose control goal with target HbA1c percentage.
    /// </summary>
    /// <param name="a1c">Target maximum HbA1c percentage.</param>
    /// <returns>A configured GoalState for glucose control.</returns>
    public static GoalState GlucoseControlGoal(decimal a1c = 7.0m) => new()
    {
        Description = GoalCodes.GlucoseControl,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        Targets =
        [
            new GoalTarget(
                FhirCode.Observations.HemoglobinA1c,
                a1c,
                "<",
                "%")
        ],
        TargetDate = DateTime.UtcNow.AddMonths(3),
        Note = $"Target: Maintain HbA1c < {a1c}%"
    };

    /// <summary>
    /// Creates a smoking cessation goal.
    /// </summary>
    /// <returns>A configured GoalState for smoking cessation.</returns>
    public static GoalState SmokingCessationGoal() => new()
    {
        Description = GoalCodes.SmokingCessation,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(3),
        Note = "Goal: Complete smoking cessation with support from cessation program"
    };

    /// <summary>
    /// Creates an exercise goal with target minutes per week.
    /// </summary>
    /// <param name="minutesPerWeek">Target exercise minutes per week.</param>
    /// <returns>A configured GoalState for exercise.</returns>
    public static GoalState ExerciseGoal(int minutesPerWeek = 150) => new()
    {
        Description = GoalCodes.IncreasedPhysicalActivity,
        Priority = "medium-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(2),
        Note = $"Goal: Achieve {minutesPerWeek} minutes of moderate exercise per week"
    };

    /// <summary>
    /// Creates a pain reduction goal with target pain score.
    /// </summary>
    /// <param name="targetScore">Target maximum pain score (0-10 scale).</param>
    /// <returns>A configured GoalState for pain reduction.</returns>
    public static GoalState PainReductionGoal(int targetScore = 3) => new()
    {
        Description = GoalCodes.PainReduction,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        Targets =
        [
            new GoalTarget(
                FhirCode.Observations.PainSeverity,
                targetScore,
                "<=",
                "{score}")
        ],
        TargetDate = DateTime.UtcNow.AddMonths(1),
        Note = $"Target: Reduce pain score to {targetScore} or below on 0-10 scale"
    };

    /// <summary>
    /// Creates a mobility improvement goal.
    /// </summary>
    /// <returns>A configured GoalState for mobility improvement.</returns>
    public static GoalState MobilityImprovementGoal() => new()
    {
        Description = GoalCodes.ImprovedMobility,
        Priority = "medium-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(3),
        Note = "Goal: Improve mobility through physical therapy and home exercises"
    };

    /// <summary>
    /// Creates a medication adherence goal.
    /// </summary>
    /// <returns>A configured GoalState for medication adherence.</returns>
    public static GoalState MedicationAdherenceGoal() => new()
    {
        Description = GoalCodes.MedicationAdherence,
        Priority = "high-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(1),
        Note = "Goal: Take all prescribed medications as directed"
    };

    /// <summary>
    /// Creates a goal that has been achieved.
    /// </summary>
    /// <param name="description">The goal description code.</param>
    /// <returns>A configured GoalState marked as achieved.</returns>
    public static GoalState AchievedGoal(FhirCode description) => new()
    {
        Description = description,
        LifecycleStatus = "completed",
        AchievementStatus = "achieved",
        Priority = "medium-priority"
    };

    /// <summary>
    /// Creates an improved nutrition goal.
    /// </summary>
    /// <returns>A configured GoalState for nutrition improvement.</returns>
    public static GoalState NutritionGoal() => new()
    {
        Description = GoalCodes.ImprovedNutrition,
        Priority = "medium-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(2),
        Note = "Goal: Follow recommended dietary guidelines and reduce processed food intake"
    };

    /// <summary>
    /// Creates an anxiety reduction goal.
    /// </summary>
    /// <returns>A configured GoalState for anxiety reduction.</returns>
    public static GoalState AnxietyReductionGoal() => new()
    {
        Description = GoalCodes.AnxietyReduction,
        Priority = "medium-priority",
        AchievementStatus = "in-progress",
        TargetDate = DateTime.UtcNow.AddMonths(3),
        Note = "Goal: Reduce anxiety symptoms through therapy and coping strategies"
    };

    #endregion
}
