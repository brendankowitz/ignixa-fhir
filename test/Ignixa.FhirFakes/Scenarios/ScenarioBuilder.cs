// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Fluent builder for composing clinical scenarios.
/// Uses a state machine pattern to build patient journeys with temporal sequencing.
/// </summary>
public sealed class ScenarioBuilder
{
    private readonly List<ScenarioState> _states = [];
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly SchemaBasedFhirResourceFaker _faker;
    private string _scenarioName = "Unnamed Scenario";
    private string _description = string.Empty;

    /// <summary>
    /// Creates a new scenario builder with the specified schema provider.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    public ScenarioBuilder(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _schemaProvider = schemaProvider;
        _faker = new SchemaBasedFhirResourceFaker(schemaProvider);
    }

    /// <summary>
    /// Sets the scenario name.
    /// </summary>
    public ScenarioBuilder WithName(string name)
    {
        _scenarioName = name;
        return this;
    }

    /// <summary>
    /// Sets the scenario description.
    /// </summary>
    public ScenarioBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    /// Adds a patient with the specified demographics.
    /// This should typically be the first state in any scenario.
    /// </summary>
    /// <param name="age">Patient age in years (optional, random if not specified).</param>
    /// <param name="gender">Patient gender ("male", "female", "other", "unknown").</param>
    /// <param name="givenName">Patient given name (optional, random if not specified).</param>
    /// <param name="familyName">Patient family name (optional, random if not specified).</param>
    /// <param name="startDate">Scenario start date (optional, defaults to 1 year ago).</param>
    public ScenarioBuilder WithPatient(
        int? age = null,
        string? gender = null,
        string? givenName = null,
        string? familyName = null,
        DateTime? startDate = null)
    {
        _states.Add(new InitialState
        {
            Name = "Initial",
            Age = age,
            Gender = gender,
            GivenName = givenName,
            FamilyName = familyName,
            StartDate = startDate
        });
        return this;
    }

    /// <summary>
    /// Adds a custom state to the scenario.
    /// </summary>
    public ScenarioBuilder AddState(ScenarioState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states.Add(state);
        return this;
    }

    /// <summary>
    /// Adds a delay to advance the simulation time.
    /// </summary>
    public ScenarioBuilder Delay(TimeSpan duration)
    {
        _states.Add(DelayState.ExactDuration(duration));
        return this;
    }

    /// <summary>
    /// Adds a delay of the specified number of days.
    /// </summary>
    public ScenarioBuilder DelayDays(int days)
    {
        _states.Add(DelayState.Days(days));
        return this;
    }

    /// <summary>
    /// Adds a delay of the specified number of weeks.
    /// </summary>
    public ScenarioBuilder DelayWeeks(int weeks)
    {
        _states.Add(DelayState.Weeks(weeks));
        return this;
    }

    /// <summary>
    /// Adds a delay of the specified number of months.
    /// </summary>
    public ScenarioBuilder DelayMonths(int months)
    {
        _states.Add(DelayState.Months(months));
        return this;
    }

    /// <summary>
    /// Adds a condition onset (disease diagnosis).
    /// </summary>
    /// <param name="code">The condition code.</param>
    /// <param name="severity">Initial severity level (1-5).</param>
    /// <param name="assignToAttribute">Attribute name to store the condition ID.</param>
    public ScenarioBuilder AddConditionOnset(FhirCode code, int severity = 1, string? assignToAttribute = null)
    {
        _states.Add(new ConditionOnsetState
        {
            Name = $"Condition_{code.Display}",
            Code = code,
            Severity = severity,
            AssignToAttribute = assignToAttribute
        });
        return this;
    }

    /// <summary>
    /// Adds an ambulatory encounter.
    /// </summary>
    public ScenarioBuilder AddEncounter(string? reason = null, int durationMinutes = 30)
    {
        _states.Add(new EncounterState
        {
            Name = $"Encounter_{reason ?? "Visit"}",
            Reason = reason,
            DurationMinutes = durationMinutes
        });
        return this;
    }

    /// <summary>
    /// Adds a wellness/checkup encounter.
    /// </summary>
    public ScenarioBuilder AddWellnessVisit(string? reason = null)
    {
        _states.Add(EncounterState.Wellness(reason));
        return this;
    }

    /// <summary>
    /// Adds an emergency encounter.
    /// </summary>
    public ScenarioBuilder AddEmergencyVisit(string? reason = null)
    {
        _states.Add(EncounterState.Emergency(reason));
        return this;
    }

    /// <summary>
    /// Adds an observation with a specific value.
    /// </summary>
    public ScenarioBuilder AddObservation(FhirCode code, decimal value, string unit, string? unitCode = null)
    {
        _states.Add(new ObservationState
        {
            Name = $"Observation_{code.Display}",
            Code = code,
            Value = value,
            Unit = unit,
            UnitCode = unitCode ?? unit
        });
        return this;
    }

    /// <summary>
    /// Adds an observation with a random value in the specified range.
    /// </summary>
    public ScenarioBuilder AddObservation(FhirCode code, decimal minValue, decimal maxValue, string unit, string? unitCode = null)
    {
        _states.Add(new ObservationState
        {
            Name = $"Observation_{code.Display}",
            Code = code,
            ValueRangeMin = minValue,
            ValueRangeMax = maxValue,
            Unit = unit,
            UnitCode = unitCode ?? unit
        });
        return this;
    }

    /// <summary>
    /// Adds an observation state.
    /// </summary>
    public ScenarioBuilder AddObservation(ObservationState observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _states.Add(observation);
        return this;
    }

    /// <summary>
    /// Adds a medication order.
    /// </summary>
    public ScenarioBuilder AddMedicationOrder(FhirCode code, bool isChronic = true, string? frequency = null, FhirCode? reasonCode = null)
    {
        _states.Add(new MedicationOrderState
        {
            Name = $"Medication_{code.Display}",
            Code = code,
            IsChronic = isChronic,
            Frequency = frequency ?? "daily",
            ReasonCode = reasonCode
        });
        return this;
    }

    /// <summary>
    /// Adds a medication order state.
    /// </summary>
    public ScenarioBuilder AddMedicationOrder(MedicationOrderState medication)
    {
        ArgumentNullException.ThrowIfNull(medication);
        _states.Add(medication);
        return this;
    }

    /// <summary>
    /// Sets an attribute value.
    /// </summary>
    public ScenarioBuilder SetAttribute(string name, object value)
    {
        _states.Add(SetAttributeState.Set(name, value));
        return this;
    }

    /// <summary>
    /// Increments a numeric attribute.
    /// </summary>
    public ScenarioBuilder IncrementAttribute(string name, int amount = 1)
    {
        _states.Add(SetAttributeState.Increment(name, amount));
        return this;
    }

    /// <summary>
    /// Adds a guard condition that must be satisfied before execution continues.
    /// </summary>
    public ScenarioBuilder AddGuard(GuardState guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _states.Add(guard);
        return this;
    }

    /// <summary>
    /// Adds a follow-up visit pattern: delay + encounter + observations.
    /// </summary>
    public ScenarioBuilder AddFollowUpVisit(int delayMonths, string reason, params ObservationState[] observations)
    {
        DelayMonths(delayMonths);
        AddEncounter(reason);
        foreach (var obs in observations)
        {
            _states.Add(obs);
        }
        return this;
    }

    #region Diagnostic Report Methods

    /// <summary>
    /// Adds a diagnostic report (lab panel or imaging report) with observations.
    /// </summary>
    /// <param name="code">The diagnostic report code.</param>
    /// <param name="observations">Optional observations as tuples of (code, value, unit).</param>
    /// <param name="conclusion">Optional conclusion text (for imaging reports).</param>
    public ScenarioBuilder AddDiagnosticReport(
        FhirCode code,
        IReadOnlyList<(FhirCode Code, decimal Value, string Unit)>? observations = null,
        string? conclusion = null)
    {
        _states.Add(new DiagnosticReportState
        {
            Name = $"DiagnosticReport_{code.Display}",
            Code = code,
            Observations = observations,
            Conclusion = conclusion
        });
        return this;
    }

    /// <summary>
    /// Adds a diagnostic report state.
    /// </summary>
    public ScenarioBuilder AddDiagnosticReport(DiagnosticReportState diagnosticReport)
    {
        ArgumentNullException.ThrowIfNull(diagnosticReport);
        _states.Add(diagnosticReport);
        return this;
    }

    /// <summary>
    /// Adds a Comprehensive Metabolic Panel (CMP) with standard lab values.
    /// </summary>
    public ScenarioBuilder AddComprehensiveMetabolicPanel()
    {
        _states.Add(DiagnosticReportState.ComprehensiveMetabolicPanel());
        return this;
    }

    /// <summary>
    /// Adds a Complete Blood Count (CBC) with standard values.
    /// </summary>
    public ScenarioBuilder AddCompleteBloodCount()
    {
        _states.Add(DiagnosticReportState.CompleteBloodCount());
        return this;
    }

    /// <summary>
    /// Adds a Lipid Panel with standard values.
    /// </summary>
    public ScenarioBuilder AddLipidPanel()
    {
        _states.Add(DiagnosticReportState.LipidPanel());
        return this;
    }

    /// <summary>
    /// Adds a Chest X-ray imaging report.
    /// </summary>
    public ScenarioBuilder AddChestXRay(string? conclusion = null)
    {
        _states.Add(DiagnosticReportState.ChestXRay(conclusion));
        return this;
    }

    #endregion

    #region Immunization Methods

    /// <summary>
    /// Adds an immunization (vaccine) record.
    /// </summary>
    /// <param name="vaccineCode">The vaccine code.</param>
    /// <param name="doseNumber">The dose number in the series (default 1).</param>
    /// <param name="series">Optional series name.</param>
    /// <param name="route">Optional route of administration (IM, oral, intranasal).</param>
    public ScenarioBuilder AddImmunization(
        FhirCode vaccineCode,
        int doseNumber = 1,
        string? series = null,
        string? route = null)
    {
        _states.Add(new ImmunizationState
        {
            Name = $"Immunization_{vaccineCode.Display}",
            Code = vaccineCode,
            DoseNumber = doseNumber,
            Series = series,
            Route = route ?? "IM"
        });
        return this;
    }

    /// <summary>
    /// Adds an immunization state.
    /// </summary>
    public ScenarioBuilder AddImmunization(ImmunizationState immunization)
    {
        ArgumentNullException.ThrowIfNull(immunization);
        _states.Add(immunization);
        return this;
    }

    /// <summary>
    /// Adds an annual influenza vaccination.
    /// </summary>
    public ScenarioBuilder AddInfluenzaVaccine()
    {
        _states.Add(ImmunizationState.InfluenzaAnnual());
        return this;
    }

    /// <summary>
    /// Adds a COVID-19 Pfizer vaccination.
    /// </summary>
    public ScenarioBuilder AddCovid19Vaccine(int doseNumber = 1)
    {
        _states.Add(ImmunizationState.Covid19Pfizer(doseNumber));
        return this;
    }

    #endregion

    #region Allergy Methods

    /// <summary>
    /// Adds an allergy or intolerance record.
    /// </summary>
    /// <param name="allergenCode">The allergen code.</param>
    /// <param name="severity">The severity (default: "moderate").</param>
    /// <param name="reactions">Optional list of reaction manifestations.</param>
    /// <param name="category">Optional category ("food", "medication", "environment", "biologic").</param>
    public ScenarioBuilder AddAllergy(
        FhirCode allergenCode,
        string? severity = null,
        IReadOnlyList<string>? reactions = null,
        string? category = null)
    {
        _states.Add(new AllergyIntoleranceState
        {
            Name = $"Allergy_{allergenCode.Display}",
            Code = allergenCode,
            Severity = severity ?? AllergyIntoleranceSeverity.Moderate,
            Reactions = reactions,
            Category = category
        });
        return this;
    }

    /// <summary>
    /// Adds an allergy intolerance state.
    /// </summary>
    public ScenarioBuilder AddAllergy(AllergyIntoleranceState allergy)
    {
        ArgumentNullException.ThrowIfNull(allergy);
        _states.Add(allergy);
        return this;
    }

    /// <summary>
    /// Adds a peanut allergy with severe reaction.
    /// </summary>
    public ScenarioBuilder AddPeanutAllergy()
    {
        _states.Add(AllergyIntoleranceState.PeanutAllergy());
        return this;
    }

    /// <summary>
    /// Adds a penicillin allergy with severe reaction.
    /// </summary>
    public ScenarioBuilder AddPenicillinAllergy()
    {
        _states.Add(AllergyIntoleranceState.PenicillinAllergy());
        return this;
    }

    #endregion

    #region Procedure Methods

    /// <summary>
    /// Adds a procedure record.
    /// </summary>
    /// <param name="procedureCode">The procedure code.</param>
    /// <param name="duration">Optional procedure duration.</param>
    /// <param name="outcome">Optional outcome text.</param>
    /// <param name="bodySite">Optional body site.</param>
    /// <param name="reason">Optional reason text.</param>
    public ScenarioBuilder AddProcedure(
        FhirCode procedureCode,
        TimeSpan? duration = null,
        string? outcome = null,
        string? bodySite = null,
        string? reason = null)
    {
        _states.Add(new ProcedureState
        {
            Name = $"Procedure_{procedureCode.Display}",
            Code = procedureCode,
            Duration = duration,
            Outcome = outcome,
            BodySite = bodySite,
            Reason = reason
        });
        return this;
    }

    /// <summary>
    /// Adds a procedure state.
    /// </summary>
    public ScenarioBuilder AddProcedure(ProcedureState procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        _states.Add(procedure);
        return this;
    }

    /// <summary>
    /// Adds a colonoscopy procedure.
    /// </summary>
    public ScenarioBuilder AddColonoscopy(string? outcome = null)
    {
        _states.Add(ProcedureState.Colonoscopy(outcome));
        return this;
    }

    /// <summary>
    /// Adds an appendectomy procedure.
    /// </summary>
    public ScenarioBuilder AddAppendectomy()
    {
        _states.Add(ProcedureState.Appendectomy());
        return this;
    }

    #endregion

    #region Condition End Methods

    /// <summary>
    /// Ends a condition by attribute reference.
    /// </summary>
    /// <param name="attributeName">The attribute name where the condition ID is stored.</param>
    /// <param name="clinicalStatus">The clinical status to set (default: "resolved").</param>
    public ScenarioBuilder EndCondition(string attributeName, string? clinicalStatus = null)
    {
        _states.Add(ConditionEndState.ByAttribute(attributeName, clinicalStatus));
        return this;
    }

    /// <summary>
    /// Ends a condition by code.
    /// </summary>
    /// <param name="code">The condition code to search for.</param>
    /// <param name="clinicalStatus">The clinical status to set (default: "resolved").</param>
    public ScenarioBuilder EndCondition(FhirCode code, string? clinicalStatus = null)
    {
        _states.Add(ConditionEndState.ByCode(code, clinicalStatus));
        return this;
    }

    /// <summary>
    /// Ends a condition state.
    /// </summary>
    public ScenarioBuilder EndCondition(ConditionEndState conditionEnd)
    {
        ArgumentNullException.ThrowIfNull(conditionEnd);
        _states.Add(conditionEnd);
        return this;
    }

    #endregion

    #region Terminal State Methods

    /// <summary>
    /// Marks the scenario as completed with the "Completed" reason.
    /// </summary>
    public ScenarioBuilder Complete()
    {
        _states.Add(TerminalState.Completed());
        return this;
    }

    /// <summary>
    /// Marks the scenario as terminated due to death.
    /// </summary>
    public ScenarioBuilder Death()
    {
        _states.Add(TerminalState.Death());
        return this;
    }

    /// <summary>
    /// Marks the scenario as terminated with a custom reason.
    /// </summary>
    public ScenarioBuilder Terminal(string reason)
    {
        _states.Add(TerminalState.Custom(reason));
        return this;
    }

    #endregion

    /// <summary>
    /// Builds and returns the completed scenario context.
    /// Executes all states in order to generate the patient journey.
    /// </summary>
    public ScenarioContext Build()
    {
        var context = new ScenarioContext
        {
            ScenarioName = _scenarioName,
            Description = _description
        };

        foreach (var state in _states)
        {
            state.Execute(context, _faker);
        }

        return context;
    }
}
