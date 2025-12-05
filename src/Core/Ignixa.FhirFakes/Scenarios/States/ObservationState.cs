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
/// State that creates an Observation resource.
/// Observations represent clinical measurements and test results.
/// </summary>
public sealed class ObservationState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the observation code.
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the observation status ("registered", "preliminary", "final", "amended", "corrected", "cancelled", "entered-in-error", "unknown").
    /// </summary>
    public string Status { get; init; } = "final";

    /// <summary>
    /// Gets or sets the numeric value for valueQuantity.
    /// </summary>
    public decimal? Value { get; init; }

    /// <summary>
    /// Gets or sets the unit for valueQuantity.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets or sets the UCUM unit code.
    /// </summary>
    public string? UnitCode { get; init; }

    /// <summary>
    /// Gets or sets the minimum value when generating a random value.
    /// </summary>
    public decimal? ValueRangeMin { get; init; }

    /// <summary>
    /// Gets or sets the maximum value when generating a random value.
    /// </summary>
    public decimal? ValueRangeMax { get; init; }

    /// <summary>
    /// Gets or sets a function to calculate the value based on context attributes.
    /// Example: severity-based A1C calculation.
    /// </summary>
    public Func<ScenarioContext, decimal>? ValueFromContext { get; init; }

    /// <summary>
    /// Gets or sets component observations (for panel results like blood pressure).
    /// </summary>
    public IReadOnlyList<ObservationComponent>? Components { get; init; }

    /// <summary>
    /// Creates an Observation resource linked to the patient and current encounter.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Observation without a Patient. Ensure InitialState runs first.");
        }

        var observation = faker.Generate("Observation");
        var node = observation.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;

        // Set category
        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/observation-category",
                        ["code"] = "vital-signs",
                        ["display"] = "Vital Signs"
                    }
                }
            }
        };

        // Set observation code
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

        // Set value
        if (Components is { Count: > 0 })
        {
            // Multi-component observation (e.g., blood pressure)
            var componentArray = new JsonArray();
            foreach (var component in Components)
            {
                var compValue = component.ValueFromContext?.Invoke(context)
                    ?? component.Value
                    ?? (component.ValueRangeMin.HasValue && component.ValueRangeMax.HasValue
                        ? _faker.Random.Decimal(component.ValueRangeMin.Value, component.ValueRangeMax.Value)
                        : 0);

                componentArray.Add(new JsonObject
                {
                    ["code"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = component.Code.System,
                                ["code"] = component.Code.Code,
                                ["display"] = component.Code.Display
                            }
                        }
                    },
                    ["valueQuantity"] = new JsonObject
                    {
                        ["value"] = compValue,
                        ["unit"] = component.Unit ?? "mmHg",
                        ["system"] = FhirCode.Systems.Ucum,
                        ["code"] = component.UnitCode ?? "mm[Hg]"
                    }
                });
            }
            node["component"] = componentArray;
        }
        else
        {
            // Simple value
            var observationValue = ValueFromContext?.Invoke(context)
                ?? Value
                ?? (ValueRangeMin.HasValue && ValueRangeMax.HasValue
                    ? _faker.Random.Decimal(ValueRangeMin.Value, ValueRangeMax.Value)
                    : 0);

            node["valueQuantity"] = new JsonObject
            {
                ["value"] = observationValue,
                ["unit"] = Unit ?? "unit",
                ["system"] = FhirCode.Systems.Ucum,
                ["code"] = UnitCode ?? Unit ?? "1"
            };
        }

        // Add to context
        context.AddObservation(observation, Code.Display);

        // NEW: Register with StateId for cross-references
        context.RegisterStateResource(StateId, observation);
    }

    /// <summary>
    /// Creates a Hemoglobin A1c observation with severity-based value.
    /// </summary>
    public static ObservationState HemoglobinA1c(string severityAttribute = "diabetes_condition_severity") => new()
    {
        Code = FhirCode.Observations.HemoglobinA1c,
        Unit = "%",
        UnitCode = "%",
        ValueFromContext = ctx =>
        {
            var severity = ctx.GetAttribute<int>(severityAttribute, 1);
            // A1C ranges: Normal < 5.7%, Prediabetes 5.7-6.4%, Diabetes >= 6.5%
            // Severity 1: 7.0-7.5%, Severity 2: 7.5-8.5%, Severity 3: 8.5-10%, etc.
            return severity switch
            {
                1 => 7.0m + new Faker().Random.Decimal(0, 0.5m),
                2 => 7.5m + new Faker().Random.Decimal(0, 1.0m),
                3 => 8.5m + new Faker().Random.Decimal(0, 1.5m),
                4 => 10.0m + new Faker().Random.Decimal(0, 1.5m),
                _ => 11.5m + new Faker().Random.Decimal(0, 2.0m)
            };
        }
    };

    /// <summary>
    /// Creates a blood glucose observation.
    /// </summary>
    public static ObservationState BloodGlucose(decimal? value = null) => new()
    {
        Code = FhirCode.Observations.BloodGlucose,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 100m,
        ValueRangeMax = value.HasValue ? null : 200m,
        Unit = "mg/dL",
        UnitCode = "mg/dL"
    };

    /// <summary>
    /// Creates a blood pressure observation with systolic and diastolic components.
    /// </summary>
    public static ObservationState BloodPressure(
        decimal? systolic = null,
        decimal? diastolic = null,
        string? systolicSeverityAttr = null,
        string? diastolicSeverityAttr = null) => new()
    {
        Code = FhirCode.Observations.BloodPressurePanel,
        Components =
        [
            new ObservationComponent
            {
                Code = FhirCode.Observations.BloodPressureSystolic,
                Value = systolic,
                ValueRangeMin = systolic.HasValue ? null : 110m,
                ValueRangeMax = systolic.HasValue ? null : 180m,
                Unit = "mmHg",
                UnitCode = "mm[Hg]",
                ValueFromContext = systolicSeverityAttr is not null ? ctx =>
                {
                    var severity = ctx.GetAttribute<int>(systolicSeverityAttr, 1);
                    return severity switch
                    {
                        1 => 135m + new Faker().Random.Decimal(0, 10m),
                        2 => 145m + new Faker().Random.Decimal(0, 10m),
                        3 => 160m + new Faker().Random.Decimal(0, 15m),
                        _ => 175m + new Faker().Random.Decimal(0, 15m)
                    };
                } : null
            },
            new ObservationComponent
            {
                Code = FhirCode.Observations.BloodPressureDiastolic,
                Value = diastolic,
                ValueRangeMin = diastolic.HasValue ? null : 70m,
                ValueRangeMax = diastolic.HasValue ? null : 110m,
                Unit = "mmHg",
                UnitCode = "mm[Hg]",
                ValueFromContext = diastolicSeverityAttr is not null ? ctx =>
                {
                    var severity = ctx.GetAttribute<int>(diastolicSeverityAttr, 1);
                    return severity switch
                    {
                        1 => 85m + new Faker().Random.Decimal(0, 5m),
                        2 => 92m + new Faker().Random.Decimal(0, 5m),
                        3 => 100m + new Faker().Random.Decimal(0, 8m),
                        _ => 110m + new Faker().Random.Decimal(0, 10m)
                    };
                } : null
            }
        ]
    };

    /// <summary>
    /// Creates a peak expiratory flow rate observation (for asthma monitoring).
    /// </summary>
    public static ObservationState PeakFlow(decimal? value = null) => new()
    {
        Code = FhirCode.Observations.PeakExpiratoryFlowRate,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 300m,
        ValueRangeMax = value.HasValue ? null : 600m,
        Unit = "L/min",
        UnitCode = "L/min"
    };

    /// <summary>
    /// Creates a fetal heart rate observation (for prenatal monitoring).
    /// </summary>
    public static ObservationState FetalHeartRate(decimal? value = null) => new()
    {
        Code = FhirCode.Observations.FetalHeartRate,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 120m,
        ValueRangeMax = value.HasValue ? null : 160m,
        Unit = "beats/minute",
        UnitCode = "/min"
    };

    /// <summary>
    /// Creates a body height observation.
    /// </summary>
    public static ObservationState BodyHeight(decimal? value = null) => new()
    {
        Code = VitalSigns.BodyHeight,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 150m,
        ValueRangeMax = value.HasValue ? null : 190m,
        Unit = "cm",
        UnitCode = "cm"
    };

    /// <summary>
    /// Creates a body weight observation.
    /// </summary>
    public static ObservationState BodyWeight(decimal? value = null) => new()
    {
        Code = VitalSigns.BodyWeight,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 50m,
        ValueRangeMax = value.HasValue ? null : 100m,
        Unit = "kg",
        UnitCode = "kg"
    };

    /// <summary>
    /// Creates a body mass index (BMI) observation.
    /// </summary>
    public static ObservationState BodyMassIndex(decimal? value = null) => new()
    {
        Code = VitalSigns.BMI,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 18.5m,
        ValueRangeMax = value.HasValue ? null : 29.9m,
        Unit = "kg/m2",
        UnitCode = "kg/m2"
    };

    /// <summary>
    /// Creates a heart rate observation.
    /// </summary>
    public static ObservationState HeartRate(decimal? value = null) => new()
    {
        Code = VitalSigns.HeartRate,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 60m,
        ValueRangeMax = value.HasValue ? null : 100m,
        Unit = "beats/minute",
        UnitCode = "/min"
    };

    /// <summary>
    /// Creates a respiratory rate observation.
    /// </summary>
    public static ObservationState RespiratoryRate(decimal? value = null) => new()
    {
        Code = VitalSigns.RespiratoryRate,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 12m,
        ValueRangeMax = value.HasValue ? null : 20m,
        Unit = "breaths/minute",
        UnitCode = "/min"
    };

    /// <summary>
    /// Creates a body temperature observation.
    /// </summary>
    public static ObservationState BodyTemperature(decimal? value = null) => new()
    {
        Code = VitalSigns.BodyTemperature,
        Value = value,
        ValueRangeMin = value.HasValue ? null : 36.5m,
        ValueRangeMax = value.HasValue ? null : 37.5m,
        Unit = "Cel",
        UnitCode = "Cel"
    };
}
