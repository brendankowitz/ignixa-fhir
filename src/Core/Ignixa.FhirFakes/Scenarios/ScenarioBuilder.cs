// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Fluent builder for creating patient-centric test scenarios.
///
/// <para>
/// <b>Design Principle: One Scenario = One Patient</b><br/>
/// ScenarioBuilder is optimized for creating a single patient with their related resources
/// (encounters, observations, conditions, etc.). For tests requiring multiple patients,
/// organizations, or complex resource graphs, use resource builders directly.
/// </para>
///
/// <para>
/// <b>When to use ScenarioBuilder:</b><br/>
/// - Creating a patient with clinical history (encounters, observations, medications)<br/>
/// - Building test data for patient-specific workflows<br/>
/// - Generating bundles for transaction/batch operations
/// </para>
///
/// <para>
/// <b>When NOT to use ScenarioBuilder:</b><br/>
/// - Multiple unrelated patients<br/>
/// - Organization hierarchies without a patient context<br/>
/// - Complex cross-patient references (use builders directly)
/// </para>
///
/// <example>
/// // GOOD: Patient-centric scenario
/// var scenario = new ScenarioBuilder(schemaProvider)
///     .WithPatient(p => p.WithAge(35).WithGender(g => g.Female))
///     .AddEncounter(...)
///     .AddObservation(...)
///     .Build();
///
/// // AVOID: Multiple unrelated patients - use PatientBuilder directly
/// var patient1 = new PatientBuilder(schemaProvider).WithAge(35).Build();
/// var patient2 = new PatientBuilder(schemaProvider).WithAge(42).Build();
/// </example>
/// </summary>
public sealed class ScenarioBuilder
{
    private readonly List<ScenarioState> _states = [];
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly SchemaBasedFhirResourceFaker _faker;
    private readonly ResourceRegistry _registry = new();
    private string _scenarioName = "Unnamed Scenario";
    private string _description = string.Empty;
    private string? _tag;
    private bool _hasPatient;
    private ReferenceFormat _referenceFormat = ReferenceFormat.UrnUuid;

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
    /// Sets a tag to be applied to all resources generated in this scenario.
    /// Useful for test isolation via the _tag search parameter.
    /// </summary>
    /// <param name="tag">The tag code to apply to all resources.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ScenarioBuilder WithTag(string? tag)
    {
        _tag = tag;
        return this;
    }

    /// <summary>
    /// Sets the reference format for generated resources.
    /// Default is UrnUuid (suitable for transaction bundles).
    /// </summary>
    /// <param name="format">The reference format to use.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public ScenarioBuilder WithReferenceFormat(ReferenceFormat format)
    {
        _referenceFormat = format;
        return this;
    }

    /// <summary>
    /// Configures the builder to use urn:uuid references (default).
    /// Suitable for transaction bundles with client-assigned IDs.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    public ScenarioBuilder WithUrnUuidReferences() => WithReferenceFormat(ReferenceFormat.UrnUuid);

    /// <summary>
    /// Configures the builder to use resolved references (ResourceType/id).
    /// Suitable for batch bundles or when resources already exist.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    public ScenarioBuilder WithResolvedReferences() => WithReferenceFormat(ReferenceFormat.Resolved);

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
        if (_hasPatient)
        {
            throw new InvalidOperationException(
                "Cannot add multiple patients to a single scenario. Each scenario supports only one patient. " +
                "To test multiple patients, create them separately and add them directly without using the scenario builder.");
        }
        _hasPatient = true;

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

    #region PatientBuilder Integration Methods

    /// <summary>
    /// Adds a patient using PatientBuilder with full configuration control.
    /// Uses PatientBuilderFactory.Create() as the base builder.
    /// This should typically be the first state in any scenario.
    /// </summary>
    /// <param name="configure">Configuration action for the PatientBuilder.</param>
    /// <param name="startDate">Scenario start date (optional, defaults to 1 year ago).</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// // Simple patient with basic demographics (suitable for simple tests)
    /// var scenario = new ScenarioBuilder(schemaProvider)
    ///     .WithPatient(p => p
    ///         .WithAge(45)
    ///         .WithGender(g => g.Male)
    ///         .WithGivenName("John")
    ///         .WithFamilyName("Smith"))
    ///     .Build();
    ///
    /// // Realistic patient from specific city (ethnically appropriate names, real demographics)
    /// var scenario = new ScenarioBuilder(schemaProvider)
    ///     .WithPatient(p => p
    ///         .FromCity(KnownCities.Boston)
    ///         .WithAge(45)
    ///         .WithRealisticBMI())
    ///     .Build();
    /// </code>
    /// </example>
    public ScenarioBuilder WithPatient(Action<PatientBuilder> configure, DateTime? startDate = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (_hasPatient)
        {
            throw new InvalidOperationException(
                "Cannot add multiple patients to a single scenario. Each scenario supports only one patient. " +
                "To test multiple patients, create them separately and add them directly without using the scenario builder.");
        }
        _hasPatient = true;

        _states.Add(new PatientBuilderState(
            builder =>
            {
                configure(builder);
                return builder;
            },
            PatientBuilderFactory.Create)
        {
            StartDate = startDate
        });
        return this;
    }

    /// <summary>
    /// Adds a patient from Seattle, Washington with realistic Pacific Northwest demographics.
    /// Uses PatientBuilderFactory.Create() with FromSeattle() configuration.
    /// </summary>
    /// <param name="configure">Optional additional configuration for the PatientBuilder.</param>
    /// <param name="startDate">Scenario start date (optional, defaults to 1 year ago).</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// var scenario = new ScenarioBuilder(schemaProvider)
    ///     .WithSeattlePatient(p => p.WithAge(35).WithRealisticBMI())
    ///     .Build();
    /// </code>
    /// </example>
    public ScenarioBuilder WithSeattlePatient(Action<PatientBuilder>? configure = null, DateTime? startDate = null)
    {
        if (_hasPatient)
        {
            throw new InvalidOperationException(
                "Cannot add multiple patients to a single scenario. Each scenario supports only one patient. " +
                "To test multiple patients, create them separately and add them directly without using the scenario builder.");
        }
        _hasPatient = true;

        _states.Add(new PatientBuilderState(
            builder =>
            {
                builder.FromSeattle();
                configure?.Invoke(builder);
                return builder;
            },
            PatientBuilderFactory.Create)
        {
            StartDate = startDate
        });
        return this;
    }

    /// <summary>
    /// Adds a patient from a specific city with realistic demographics.
    /// Uses PatientBuilderFactory.Create() with FromCity() configuration.
    /// </summary>
    /// <param name="city">The city demographics (use KnownCities class for predefined cities).</param>
    /// <param name="configure">Optional additional configuration for the PatientBuilder.</param>
    /// <param name="startDate">Scenario start date (optional, defaults to 1 year ago).</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// var scenario = new ScenarioBuilder(schemaProvider)
    ///     .WithPatientFromCity(KnownCities.NewYork, p => p.WithAge(28))
    ///     .Build();
    ///
    /// // International cities
    /// var scenario = new ScenarioBuilder(schemaProvider)
    ///     .WithPatientFromCity(KnownCities.Melbourne)
    ///     .Build();
    /// </code>
    /// </example>
    public ScenarioBuilder WithPatientFromCity(
        CityDemographics city,
        Action<PatientBuilder>? configure = null,
        DateTime? startDate = null)
    {
        ArgumentNullException.ThrowIfNull(city);

        if (_hasPatient)
        {
            throw new InvalidOperationException(
                "Cannot add multiple patients to a single scenario. Each scenario supports only one patient. " +
                "To test multiple patients, create them separately and add them directly without using the scenario builder.");
        }
        _hasPatient = true;

        _states.Add(new PatientBuilderState(
            builder =>
            {
                builder.FromCity(city);
                configure?.Invoke(builder);
                return builder;
            },
            PatientBuilderFactory.Create)
        {
            StartDate = startDate
        });
        return this;
    }

    #endregion

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
    /// Adds a reusable sub-scenario fragment.
    /// Enables composition of scenarios from common clinical patterns.
    /// </summary>
    /// <param name="subScenario">The sub-scenario builder function to execute.</param>
    /// <param name="name">Optional name for the sub-scenario state (for debugging/logging).</param>
    /// <returns>The current builder for fluent chaining.</returns>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Vitals")
    /// .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Order Labs")
    /// </code>
    /// </remarks>
    public ScenarioBuilder AddSubScenario(Func<ScenarioBuilder, ScenarioBuilder> subScenario, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(subScenario);
        _states.Add(new CallSubScenarioState
        {
            Name = name ?? "SubScenario",
            SubScenario = subScenario
        });
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

    #region Coverage Methods

    /// <summary>
    /// Adds a coverage (insurance) record.
    /// </summary>
    /// <param name="memberId">Optional member ID (auto-generated if not specified).</param>
    /// <param name="relationship">Relationship to subscriber (default: "self").</param>
    /// <param name="typeCode">Coverage type code (e.g., "EHCPOL", "PUBLICPOL").</param>
    /// <param name="typeDisplay">Coverage type display.</param>
    /// <param name="startDate">Coverage start date (defaults to patient birth date).</param>
    /// <param name="endDate">Coverage end date (null = ongoing).</param>
    public ScenarioBuilder AddCoverage(
        string? memberId = null,
        string? relationship = null,
        string? typeCode = null,
        string? typeDisplay = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        _states.Add(new CoverageState
        {
            Name = "Coverage",
            MemberId = memberId,
            Relationship = relationship ?? "self",
            TypeCode = typeCode,
            TypeDisplay = typeDisplay,
            StartDate = startDate,
            EndDate = endDate
        });
        return this;
    }

    /// <summary>
    /// Adds a coverage state.
    /// </summary>
    public ScenarioBuilder AddCoverage(CoverageState coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        _states.Add(coverage);
        return this;
    }

    /// <summary>
    /// Adds a self-insured coverage (patient is the subscriber).
    /// </summary>
    public ScenarioBuilder AddSelfCoverage()
    {
        _states.Add(CoverageState.SelfCoverage());
        return this;
    }

    /// <summary>
    /// Adds a child coverage (patient is a dependent on parent's plan).
    /// </summary>
    /// <param name="dependent">The dependent number (default 1).</param>
    public ScenarioBuilder AddChildCoverage(int dependent = 1)
    {
        _states.Add(CoverageState.ChildCoverage(dependent));
        return this;
    }

    /// <summary>
    /// Adds a Medicare coverage (US public healthcare for seniors).
    /// </summary>
    public ScenarioBuilder AddMedicareCoverage()
    {
        _states.Add(CoverageState.Medicare());
        return this;
    }

    /// <summary>
    /// Adds a Medicaid coverage (US public healthcare for low-income).
    /// </summary>
    public ScenarioBuilder AddMedicaidCoverage()
    {
        _states.Add(CoverageState.Medicaid());
        return this;
    }

    #endregion

    #region Practitioner Methods

    /// <summary>
    /// Adds a practitioner (healthcare provider) to the scenario.
    /// </summary>
    /// <param name="specialty">The specialty code from Specialties constants.</param>
    /// <param name="givenName">Optional given (first) name.</param>
    /// <param name="familyName">Optional family (last) name.</param>
    /// <param name="gender">Optional gender ("male", "female", "other").</param>
    /// <param name="npiNumber">Optional NPI number (auto-generated if not specified).</param>
    public ScenarioBuilder AddPractitioner(
        FhirCode specialty,
        string? givenName = null,
        string? familyName = null,
        string? gender = null,
        string? npiNumber = null)
    {
        _states.Add(new PractitionerState
        {
            Name = $"Practitioner_{specialty.Display}",
            Specialty = specialty,
            GivenName = givenName,
            FamilyName = familyName,
            Gender = gender,
            NpiNumber = npiNumber
        });
        return this;
    }

    /// <summary>
    /// Adds a practitioner state.
    /// </summary>
    public ScenarioBuilder AddPractitioner(PractitionerState practitioner)
    {
        ArgumentNullException.ThrowIfNull(practitioner);
        _states.Add(practitioner);
        return this;
    }

    /// <summary>
    /// Adds a family medicine practitioner (primary care physician).
    /// </summary>
    public ScenarioBuilder AddFamilyPractitioner()
    {
        _states.Add(PractitionerState.FamilyPractitioner());
        return this;
    }

    /// <summary>
    /// Adds a pediatrician (children's doctor).
    /// </summary>
    public ScenarioBuilder AddPediatrician()
    {
        _states.Add(PractitionerState.Pediatrician());
        return this;
    }

    /// <summary>
    /// Adds a cardiologist (heart specialist).
    /// </summary>
    public ScenarioBuilder AddCardiologist()
    {
        _states.Add(PractitionerState.Cardiologist());
        return this;
    }

    /// <summary>
    /// Adds an emergency medicine physician.
    /// </summary>
    public ScenarioBuilder AddEmergencyPhysician()
    {
        _states.Add(PractitionerState.EmergencyPhysician());
        return this;
    }

    /// <summary>
    /// Adds a general surgeon.
    /// </summary>
    public ScenarioBuilder AddSurgeon()
    {
        _states.Add(PractitionerState.Surgeon());
        return this;
    }

    /// <summary>
    /// Adds a registered nurse.
    /// </summary>
    public ScenarioBuilder AddNurse()
    {
        _states.Add(PractitionerState.Nurse());
        return this;
    }

    /// <summary>
    /// Adds a nurse practitioner.
    /// </summary>
    public ScenarioBuilder AddNursePractitioner()
    {
        _states.Add(PractitionerState.NursePractitioner());
        return this;
    }

    /// <summary>
    /// Adds an internal medicine physician.
    /// </summary>
    public ScenarioBuilder AddInternist()
    {
        _states.Add(PractitionerState.Internist());
        return this;
    }

    #endregion

    #region Organization Methods

    /// <summary>
    /// Adds an organization (healthcare facility, payer, etc.) to the scenario.
    /// </summary>
    /// <param name="name">The organization name.</param>
    /// <param name="type">Optional organization type code.</param>
    /// <param name="npiNumber">Optional NPI number (auto-generated if not specified).</param>
    /// <param name="taxId">Optional Tax ID (auto-generated if not specified).</param>
    /// <param name="setAsCurrent">Whether to set this as the current organization context.</param>
    public ScenarioBuilder AddOrganization(
        string name,
        FhirCode? type = null,
        string? npiNumber = null,
        string? taxId = null,
        bool setAsCurrent = true)
    {
        _states.Add(new OrganizationState
        {
            Name = $"Organization_{name}",
            OrganizationName = name,
            Type = type,
            NpiNumber = npiNumber,
            TaxId = taxId,
            SetAsCurrent = setAsCurrent
        });
        return this;
    }

    /// <summary>
    /// Adds an organization state.
    /// </summary>
    public ScenarioBuilder AddOrganization(OrganizationState organization)
    {
        ArgumentNullException.ThrowIfNull(organization);
        _states.Add(organization);
        return this;
    }

    /// <summary>
    /// Adds a hospital organization.
    /// </summary>
    /// <param name="name">Optional hospital name (auto-generated if not specified).</param>
    public ScenarioBuilder AddHospital(string? name = null)
    {
        _states.Add(OrganizationState.Hospital(name));
        return this;
    }

    /// <summary>
    /// Adds a family practice clinic organization.
    /// </summary>
    /// <param name="name">Optional clinic name (auto-generated if not specified).</param>
    public ScenarioBuilder AddClinicFamilyPractice(string? name = null)
    {
        _states.Add(OrganizationState.ClinicFamilyPractice(name));
        return this;
    }

    /// <summary>
    /// Adds an emergency department organization.
    /// </summary>
    /// <param name="name">Optional department name (auto-generated if not specified).</param>
    public ScenarioBuilder AddEmergencyDepartment(string? name = null)
    {
        _states.Add(OrganizationState.EmergencyDepartment(name));
        return this;
    }

    /// <summary>
    /// Adds an insurance company organization.
    /// </summary>
    /// <param name="name">Optional company name (auto-generated if not specified).</param>
    public ScenarioBuilder AddInsuranceCompany(string? name = null)
    {
        _states.Add(OrganizationState.InsuranceCompany(name));
        return this;
    }

    /// <summary>
    /// Adds a clinical laboratory organization.
    /// </summary>
    /// <param name="name">Optional lab name (auto-generated if not specified).</param>
    public ScenarioBuilder AddLaboratory(string? name = null)
    {
        _states.Add(OrganizationState.Laboratory(name));
        return this;
    }

    /// <summary>
    /// Adds a pharmacy chain organization.
    /// </summary>
    /// <param name="name">Optional pharmacy name (auto-generated if not specified).</param>
    public ScenarioBuilder AddPharmacy(string? name = null)
    {
        _states.Add(OrganizationState.PharmacyChain(name));
        return this;
    }

    /// <summary>
    /// Adds an imaging center organization.
    /// </summary>
    /// <param name="name">Optional center name (auto-generated if not specified).</param>
    public ScenarioBuilder AddImagingCenter(string? name = null)
    {
        _states.Add(OrganizationState.ImagingCenter(name));
        return this;
    }

    /// <summary>
    /// Adds a payer organization (e.g., Medicare, Medicaid).
    /// </summary>
    /// <param name="name">Optional payer name (auto-generated if not specified).</param>
    public ScenarioBuilder AddPayerOrganization(string? name = null)
    {
        _states.Add(OrganizationState.Payer(name));
        return this;
    }

    /// <summary>
    /// Adds an urgent care clinic organization.
    /// </summary>
    /// <param name="name">Optional clinic name (auto-generated if not specified).</param>
    public ScenarioBuilder AddUrgentCare(string? name = null)
    {
        _states.Add(OrganizationState.UrgentCare(name));
        return this;
    }

    #endregion

    #region Service Request Methods

    /// <summary>
    /// Adds a service request (order for labs, imaging, or referral).
    /// </summary>
    /// <param name="code">The service code (from ServiceRequestCodes).</param>
    /// <param name="category">Optional category ("laboratory", "imaging", "referral", etc.).</param>
    /// <param name="priority">Optional priority ("routine", "urgent", "asap", "stat").</param>
    /// <param name="reasonCode">Optional reason code for the request.</param>
    public ScenarioBuilder AddServiceRequest(
        FhirCode code,
        string? category = null,
        string? priority = null,
        FhirCode? reasonCode = null)
    {
        _states.Add(new ServiceRequestState
        {
            Name = $"ServiceRequest_{code.Display}",
            Code = code,
            Category = category,
            Priority = priority ?? "routine",
            ReasonCode = reasonCode
        });
        return this;
    }

    /// <summary>
    /// Adds a service request state.
    /// </summary>
    public ScenarioBuilder AddServiceRequest(ServiceRequestState serviceRequest)
    {
        ArgumentNullException.ThrowIfNull(serviceRequest);
        _states.Add(serviceRequest);
        return this;
    }

    /// <summary>
    /// Adds a laboratory order.
    /// </summary>
    /// <param name="code">The laboratory test code (from ServiceRequestCodes.Laboratory).</param>
    /// <param name="priority">Optional priority ("routine", "urgent", "stat").</param>
    public ScenarioBuilder AddLabOrder(FhirCode code, string? priority = null)
    {
        _states.Add(new ServiceRequestState
        {
            Name = $"LabOrder_{code.Display}",
            Code = code,
            Category = "laboratory",
            Priority = priority ?? "routine"
        });
        return this;
    }

    /// <summary>
    /// Adds an imaging order.
    /// </summary>
    /// <param name="code">The imaging study code (from ServiceRequestCodes.Imaging).</param>
    /// <param name="priority">Optional priority ("routine", "urgent", "stat").</param>
    public ScenarioBuilder AddImagingOrder(FhirCode code, string? priority = null)
    {
        _states.Add(new ServiceRequestState
        {
            Name = $"ImagingOrder_{code.Display}",
            Code = code,
            Category = "imaging",
            Priority = priority ?? "routine"
        });
        return this;
    }

    /// <summary>
    /// Adds a specialist referral.
    /// </summary>
    /// <param name="code">The referral code (from ServiceRequestCodes.Referrals).</param>
    /// <param name="reasonCode">Optional reason code for the referral.</param>
    public ScenarioBuilder AddReferral(FhirCode code, FhirCode? reasonCode = null)
    {
        _states.Add(new ServiceRequestState
        {
            Name = $"Referral_{code.Display}",
            Code = code,
            Category = "referral",
            Priority = "routine",
            ReasonCode = reasonCode
        });
        return this;
    }

    /// <summary>
    /// Adds a CBC (Complete Blood Count) lab order.
    /// </summary>
    public ScenarioBuilder AddCBCOrder()
    {
        _states.Add(ServiceRequestState.CBCOrder());
        return this;
    }

    /// <summary>
    /// Adds a Comprehensive Metabolic Panel lab order.
    /// </summary>
    public ScenarioBuilder AddComprehensiveMetabolicPanelOrder()
    {
        _states.Add(ServiceRequestState.ComprehensiveMetabolicPanelOrder());
        return this;
    }

    /// <summary>
    /// Adds a Lipid Panel lab order.
    /// </summary>
    public ScenarioBuilder AddLipidPanelOrder()
    {
        _states.Add(ServiceRequestState.LipidPanelOrder());
        return this;
    }

    /// <summary>
    /// Adds a Hemoglobin A1c lab order for diabetes monitoring.
    /// </summary>
    public ScenarioBuilder AddHemoglobinA1cOrder()
    {
        _states.Add(ServiceRequestState.HemoglobinA1cOrder());
        return this;
    }

    /// <summary>
    /// Adds a Chest X-ray imaging order.
    /// </summary>
    public ScenarioBuilder AddChestXRayOrder()
    {
        _states.Add(ServiceRequestState.ChestXRayOrder());
        return this;
    }

    /// <summary>
    /// Adds a CT Chest imaging order.
    /// </summary>
    public ScenarioBuilder AddCTChestOrder()
    {
        _states.Add(ServiceRequestState.CTChestOrder());
        return this;
    }

    /// <summary>
    /// Adds an MRI Brain imaging order.
    /// </summary>
    public ScenarioBuilder AddMRIBrainOrder()
    {
        _states.Add(ServiceRequestState.MRIBrainOrder());
        return this;
    }

    /// <summary>
    /// Adds a Mammogram screening order.
    /// </summary>
    public ScenarioBuilder AddMammogramOrder()
    {
        _states.Add(ServiceRequestState.MammogramOrder());
        return this;
    }

    /// <summary>
    /// Adds a Cardiology consultation referral.
    /// </summary>
    public ScenarioBuilder AddCardiologyReferral()
    {
        _states.Add(ServiceRequestState.CardiologyReferral());
        return this;
    }

    /// <summary>
    /// Adds an Orthopedic consultation referral.
    /// </summary>
    public ScenarioBuilder AddOrthopedicReferral()
    {
        _states.Add(ServiceRequestState.OrthopedicReferral());
        return this;
    }

    /// <summary>
    /// Adds a Physical Therapy referral.
    /// </summary>
    public ScenarioBuilder AddPhysicalTherapyReferral()
    {
        _states.Add(ServiceRequestState.PhysicalTherapyReferral());
        return this;
    }

    /// <summary>
    /// Adds a Psychiatry consultation referral.
    /// </summary>
    public ScenarioBuilder AddPsychiatryReferral()
    {
        _states.Add(ServiceRequestState.PsychiatryReferral());
        return this;
    }

    /// <summary>
    /// Adds an urgent CBC lab order.
    /// </summary>
    public ScenarioBuilder AddUrgentCBCOrder()
    {
        _states.Add(ServiceRequestState.UrgentCBCOrder());
        return this;
    }

    /// <summary>
    /// Adds a stat Metabolic Panel order (for emergencies).
    /// </summary>
    public ScenarioBuilder AddStatMetabolicPanelOrder()
    {
        _states.Add(ServiceRequestState.StatMetabolicPanelOrder());
        return this;
    }

    #endregion

    #region Goal Methods

    /// <summary>
    /// Adds a goal (desired health outcome) to the scenario.
    /// Goals define measurable objectives that care plans and interventions aim to achieve.
    /// </summary>
    /// <param name="description">The goal description code (SNOMED CT).</param>
    /// <param name="priority">Optional priority ("high-priority", "medium-priority", "low-priority").</param>
    /// <param name="targetDate">Optional target date for achieving the goal.</param>
    /// <param name="assignToAttribute">Optional attribute name to store the goal ID for later reference.</param>
    public ScenarioBuilder AddGoal(
        FhirCode description,
        string? priority = null,
        DateTime? targetDate = null,
        string? assignToAttribute = null)
    {
        _states.Add(new GoalState
        {
            Name = $"Goal_{description.Display}",
            Description = description,
            Priority = priority ?? "medium-priority",
            TargetDate = targetDate,
            AssignToAttribute = assignToAttribute
        });
        return this;
    }

    /// <summary>
    /// Adds a goal state.
    /// </summary>
    public ScenarioBuilder AddGoal(GoalState goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _states.Add(goal);
        return this;
    }

    /// <summary>
    /// Adds a weight loss goal with target pounds.
    /// </summary>
    /// <param name="pounds">Target weight loss in pounds.</param>
    public ScenarioBuilder AddWeightLossGoal(decimal pounds)
    {
        _states.Add(GoalState.WeightLossGoal(pounds));
        return this;
    }

    /// <summary>
    /// Adds a blood pressure control goal.
    /// </summary>
    /// <param name="systolic">Target maximum systolic blood pressure (default: 130 mmHg).</param>
    public ScenarioBuilder AddBloodPressureControlGoal(int systolic = 130)
    {
        _states.Add(GoalState.BloodPressureControlGoal(systolic));
        return this;
    }

    /// <summary>
    /// Adds a glucose control goal (HbA1c target).
    /// </summary>
    /// <param name="a1c">Target maximum HbA1c percentage (default: 7.0%).</param>
    public ScenarioBuilder AddGlucoseControlGoal(decimal a1c = 7.0m)
    {
        _states.Add(GoalState.GlucoseControlGoal(a1c));
        return this;
    }

    /// <summary>
    /// Adds a smoking cessation goal.
    /// </summary>
    public ScenarioBuilder AddSmokingCessationGoal()
    {
        _states.Add(GoalState.SmokingCessationGoal());
        return this;
    }

    /// <summary>
    /// Adds an exercise goal with target minutes per week.
    /// </summary>
    /// <param name="minutesPerWeek">Target exercise minutes per week (default: 150).</param>
    public ScenarioBuilder AddExerciseGoal(int minutesPerWeek = 150)
    {
        _states.Add(GoalState.ExerciseGoal(minutesPerWeek));
        return this;
    }

    /// <summary>
    /// Adds a pain reduction goal.
    /// </summary>
    /// <param name="targetScore">Target maximum pain score on 0-10 scale (default: 3).</param>
    public ScenarioBuilder AddPainReductionGoal(int targetScore = 3)
    {
        _states.Add(GoalState.PainReductionGoal(targetScore));
        return this;
    }

    /// <summary>
    /// Adds a mobility improvement goal.
    /// </summary>
    public ScenarioBuilder AddMobilityImprovementGoal()
    {
        _states.Add(GoalState.MobilityImprovementGoal());
        return this;
    }

    /// <summary>
    /// Adds a medication adherence goal.
    /// </summary>
    public ScenarioBuilder AddMedicationAdherenceGoal()
    {
        _states.Add(GoalState.MedicationAdherenceGoal());
        return this;
    }

    #endregion

    #region CarePlan Methods

    /// <summary>
    /// Adds a care plan to the scenario.
    /// CarePlans define activities and interventions to achieve goals and coordinate care.
    /// </summary>
    /// <param name="title">The human-readable title of the care plan.</param>
    /// <param name="description">Optional description/summary of the care plan.</param>
    /// <param name="assignToAttribute">Optional attribute name to store the care plan ID for later reference.</param>
    public ScenarioBuilder AddCarePlan(
        string title,
        string? description = null,
        string? assignToAttribute = null)
    {
        _states.Add(new CarePlanState
        {
            Name = $"CarePlan_{title}",
            Title = title,
            Description = description,
            AssignToAttribute = assignToAttribute
        });
        return this;
    }

    /// <summary>
    /// Adds a care plan state.
    /// </summary>
    public ScenarioBuilder AddCarePlan(CarePlanState carePlan)
    {
        ArgumentNullException.ThrowIfNull(carePlan);
        _states.Add(carePlan);
        return this;
    }

    /// <summary>
    /// Adds a diabetes management care plan.
    /// </summary>
    public ScenarioBuilder AddDiabetesManagementPlan()
    {
        _states.Add(CarePlanState.DiabetesManagementPlan());
        return this;
    }

    /// <summary>
    /// Adds a hypertension management care plan.
    /// </summary>
    public ScenarioBuilder AddHypertensionManagementPlan()
    {
        _states.Add(CarePlanState.HypertensionManagementPlan());
        return this;
    }

    /// <summary>
    /// Adds a cardiac rehabilitation care plan.
    /// </summary>
    public ScenarioBuilder AddCardiacRehabilitationPlan()
    {
        _states.Add(CarePlanState.CardiacRehabilitationPlan());
        return this;
    }

    /// <summary>
    /// Adds a weight loss care plan.
    /// </summary>
    public ScenarioBuilder AddWeightLossPlan()
    {
        _states.Add(CarePlanState.WeightLossPlan());
        return this;
    }

    /// <summary>
    /// Adds a chronic pain management care plan.
    /// </summary>
    public ScenarioBuilder AddChronicPainManagementPlan()
    {
        _states.Add(CarePlanState.ChronicPainManagementPlan());
        return this;
    }

    /// <summary>
    /// Adds a post-surgical care plan.
    /// </summary>
    public ScenarioBuilder AddPostSurgicalCarePlan()
    {
        _states.Add(CarePlanState.PostSurgicalCarePlan());
        return this;
    }

    /// <summary>
    /// Adds a smoking cessation care plan.
    /// </summary>
    public ScenarioBuilder AddSmokingCessationPlan()
    {
        _states.Add(CarePlanState.SmokingCessationPlan());
        return this;
    }

    /// <summary>
    /// Adds a mental health care plan.
    /// </summary>
    public ScenarioBuilder AddMentalHealthCarePlan()
    {
        _states.Add(CarePlanState.MentalHealthCarePlan());
        return this;
    }

    #endregion

    #region CareTeam Methods

    /// <summary>
    /// Adds a care team to the scenario.
    /// CareTeams coordinate care across multiple practitioners for a patient.
    /// </summary>
    /// <param name="teamName">The care team name.</param>
    /// <param name="status">The care team status (default: "active").</param>
    /// <param name="category">Optional category code.</param>
    /// <param name="participantStateIds">Optional StateIds of practitioners to include.</param>
    public ScenarioBuilder AddCareTeam(
        string teamName,
        string status = "active",
        FhirCode? category = null,
        IReadOnlyList<string>? participantStateIds = null)
    {
        _states.Add(new CareTeamState
        {
            Name = $"CareTeam_{teamName}",
            TeamName = teamName,
            Status = status,
            Category = category,
            ParticipantStateIds = participantStateIds
        });
        return this;
    }

    /// <summary>
    /// Adds a care team state to the scenario.
    /// </summary>
    public ScenarioBuilder AddCareTeam(CareTeamState careTeam)
    {
        ArgumentNullException.ThrowIfNull(careTeam);
        _states.Add(careTeam);
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

    #region Probabilistic Branching Methods

    /// <summary>
    /// Adds a probabilistic branch with multiple weighted options.
    /// Used to model realistic disease onset rates, condition prevalence, and epidemiological data.
    /// </summary>
    /// <param name="branches">
    /// Variable arguments of tuples containing probability and state.
    /// Each probability must be between 0.0 and 1.0, and the sum should equal 1.0 (within tolerance).
    /// </param>
    /// <returns>The current builder for fluent chaining.</returns>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// // Model appendicitis prevalence: 8.6% develop condition, 91.4% remain healthy
    /// .AddProbabilisticBranch(
    ///     (0.086, new ConditionOnsetState { Code = SnoCodes.Appendicitis }),
    ///     (0.914, new DelayState { Duration = TimeSpan.Zero }) // No-op for healthy path
    /// )
    /// </code>
    /// </remarks>
    public ScenarioBuilder AddProbabilisticBranch(params (double probability, ScenarioState state)[] branches)
    {
        _states.Add(ProbabilisticBranchState.Create(branches));
        return this;
    }

    /// <summary>
    /// Adds a binary probabilistic branch (e.g., disease occurs vs. stays healthy).
    /// Automatically calculates the complement probability for the second state.
    /// </summary>
    /// <param name="probability">Probability of executing the first state (0.0-1.0).</param>
    /// <param name="trueState">State to execute with the specified probability.</param>
    /// <param name="falseState">State to execute with probability (1.0 - probability).</param>
    /// <returns>The current builder for fluent chaining.</returns>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// // 15% chance of developing hypertension
    /// .AddProbabilisticBranch(
    ///     0.15,
    ///     new ConditionOnsetState { Code = SnoCodes.Hypertension },
    ///     new DelayState { Duration = TimeSpan.Zero } // Healthy path
    /// )
    /// </code>
    /// </remarks>
    public ScenarioBuilder AddProbabilisticBranch(double probability, ScenarioState trueState, ScenarioState falseState)
    {
        _states.Add(ProbabilisticBranchState.Binary(probability, trueState, falseState));
        return this;
    }

    #endregion

    /// <summary>
    /// Gets the internal FHIR schema provider.
    /// Used internally for sub-scenario composition.
    /// </summary>
    internal IFhirSchemaProvider SchemaProvider => _schemaProvider;

    /// <summary>
    /// Gets the list of states currently in the builder.
    /// Used internally for sub-scenario composition.
    /// </summary>
    internal IReadOnlyList<ScenarioState> GetStates() => _states;

    /// <summary>
    /// Builds and returns the completed scenario context.
    /// Executes all states in order to generate the patient journey.
    /// Optionally rewrites references based on configured format.
    /// </summary>
    public ScenarioContext Build()
    {
        var context = new ScenarioContext
        {
            ScenarioName = _scenarioName,
            Description = _description
        };

        // Pass registry to context for automatic registration
        context.SetRegistry(_registry);

        // Configure faker with tag before executing states
        _faker.WithTag(_tag);

        foreach (var state in _states)
        {
            state.Execute(context, _faker);
        }

        // Always rewrite references to match the desired format
        // Resources are created with ResourceType/id format by default
        // and need to be rewritten to either urn:uuid or resolved format
        var rewriter = new ReferenceRewriterService(_schemaProvider.ReferenceMetadataProvider);
        rewriter.RewriteReferences(
            context.AllResources,
            _registry.All,
            _referenceFormat);

        return context;
    }
}
