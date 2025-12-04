// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Orchestrates the simulation of a patient's entire life from birth to a target age.
/// This is the core class for Layer 3 (Patient Lifecycles) that coordinates multiple
/// lifecycle events such as wellness visits, immunizations, and probabilistic disease onset.
/// </summary>
/// <remarks>
/// <para>
/// The PatientLifecycleGenerator uses a fluent builder pattern to configure:
/// <list type="bullet">
///   <item><description>Birth year and gender for patient demographics</description></item>
///   <item><description>Wellness schedules (pediatric and/or adult)</description></item>
///   <item><description>Immunization schedules per CDC recommendations</description></item>
///   <item><description>Probabilistic conditions based on epidemiological data</description></item>
/// </list>
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var lifecycle = new PatientLifecycleGenerator(schemaProvider)
///     .WithBirthYear(1980)
///     .WithGender("male")
///     .AddWellnessSchedule(pediatric: true, adult: true)
///     .AddImmunizationSchedule()
///     .AddProbabilisticCondition("Asthma", onsetAges: 1..17, probability: 0.263)
///     .AddProbabilisticCondition("Diabetes", onsetAges: 35..65, probability: 0.15)
///     .SimulateUntilAge(78);
/// </code>
/// </para>
/// <para>
/// The simulation loop progresses year by year from birth (age 0) to the target age,
/// executing all applicable lifecycle events at each age. Events determine their
/// applicability based on age and may be one-time (conditions) or recurring (wellness visits).
/// </para>
/// </remarks>
/// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class PatientLifecycleGenerator(IFhirSchemaProvider schemaProvider)
{
    /// <summary>
    /// Minimum valid birth year for patient generation.
    /// </summary>
    private const int MinBirthYear = 1900;

    /// <summary>
    /// Maximum valid birth year for patient generation.
    /// </summary>
    private const int MaxBirthYear = 2100;

    private readonly IFhirSchemaProvider _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
    private readonly List<ILifecycleEvent> _events = [];

    private int _birthYear = DateTime.UtcNow.Year - 30; // Default to 30 years ago
    private string _gender = "unknown";
    private string? _givenName;
    private string? _familyName;
    private string? _zipCode;
    private string? _areaCode;

    /// <summary>
    /// Gets the configured birth year for the patient.
    /// </summary>
    public int BirthYear => _birthYear;

    /// <summary>
    /// Gets the configured gender for the patient.
    /// </summary>
    public string Gender => _gender;

    /// <summary>
    /// Gets the configured given name for the patient, or null if not set (random will be generated).
    /// </summary>
    public string? GivenName => _givenName;

    /// <summary>
    /// Gets the configured family name for the patient, or null if not set (random will be generated).
    /// </summary>
    public string? FamilyName => _familyName;

    /// <summary>
    /// Gets the collection of lifecycle events that will be evaluated during simulation.
    /// </summary>
    public IReadOnlyList<ILifecycleEvent> Events => _events;

    /// <summary>
    /// Sets the birth year for the patient.
    /// </summary>
    /// <param name="year">The birth year (must be between 1900 and 2100).</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="year"/> is less than 1900 or greater than 2100.
    /// </exception>
    public PatientLifecycleGenerator WithBirthYear(int year)
    {
        if (year < MinBirthYear || year > MaxBirthYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(year),
                year,
                $"Birth year must be between {MinBirthYear} and {MaxBirthYear}.");
        }

        _birthYear = year;
        return this;
    }

    /// <summary>
    /// Sets the gender for the patient.
    /// </summary>
    /// <param name="gender">
    /// The patient's administrative gender. Valid values are "male", "female", "other", or "unknown".
    /// </param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gender"/> is null.
    /// </exception>
    public PatientLifecycleGenerator WithGender(string gender)
    {
        ArgumentNullException.ThrowIfNull(gender);
        _gender = gender;
        return this;
    }

    /// <summary>
    /// Sets the given name for the patient.
    /// </summary>
    /// <param name="givenName">The patient's given (first) name. If null, a random name will be generated.</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    public PatientLifecycleGenerator WithGivenName(string? givenName)
    {
        _givenName = givenName;
        return this;
    }

    /// <summary>
    /// Sets the family name for the patient.
    /// </summary>
    /// <param name="familyName">The patient's family (last) name. If null, a random name will be generated.</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    public PatientLifecycleGenerator WithFamilyName(string? familyName)
    {
        _familyName = familyName;
        return this;
    }

    /// <summary>
    /// Sets the zip code for the patient's address.
    /// </summary>
    /// <param name="zipCode">The 5-digit zip code. If null, no address will be generated.</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    public PatientLifecycleGenerator WithZipCode(string? zipCode)
    {
        _zipCode = zipCode;
        return this;
    }

    /// <summary>
    /// Sets the area code for the patient's phone number.
    /// </summary>
    /// <param name="areaCode">The 3-digit area code. If null, no phone number will be generated.</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    public PatientLifecycleGenerator WithAreaCode(string? areaCode)
    {
        _areaCode = areaCode;
        return this;
    }

    /// <summary>
    /// Adds wellness visit schedules to the lifecycle simulation.
    /// </summary>
    /// <param name="pediatric">
    /// When <c>true</c>, adds pediatric wellness visits at ages 1, 2, 4, 6, 8, 10, 12, 14, 16, and 18.
    /// </param>
    /// <param name="adult">
    /// When <c>true</c>, adds annual adult wellness visits starting at age 18.
    /// </param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <remarks>
    /// Wellness schedules generate Encounter resources with standard wellness visit codes
    /// and may include vital signs observations appropriate for the patient's age.
    /// </remarks>
    public PatientLifecycleGenerator AddWellnessSchedule(bool pediatric, bool adult)
    {
        if (pediatric)
        {
            _events.Add(new PediatricWellnessSchedule());
        }

        if (adult)
        {
            _events.Add(new AdultWellnessSchedule());
        }

        return this;
    }

    /// <summary>
    /// Adds the standard immunization schedule to the lifecycle simulation.
    /// </summary>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <remarks>
    /// The immunization schedule follows CDC recommendations and generates
    /// Immunization resources at appropriate ages for vaccines such as:
    /// <list type="bullet">
    ///   <item><description>HepB series (birth, 1-2 months, 6-18 months)</description></item>
    ///   <item><description>DTaP series (2, 4, 6, 15-18 months, 4-6 years)</description></item>
    ///   <item><description>MMR (12-15 months, 4-6 years)</description></item>
    ///   <item><description>Annual influenza (starting at 6 months)</description></item>
    /// </list>
    /// </remarks>
    public PatientLifecycleGenerator AddImmunizationSchedule()
    {
        _events.Add(new ImmunizationScheduleEvent());
        return this;
    }

    /// <summary>
    /// Adds a probabilistic condition that may onset within a specified age range.
    /// </summary>
    /// <param name="conditionName">
    /// The name of the condition (e.g., "Asthma", "Diabetes", "Hypertension").
    /// Used for logging and tracking.
    /// </param>
    /// <param name="onsetAges">
    /// The age range (inclusive) during which the condition may onset.
    /// Use C# range syntax: <c>1..17</c> for ages 1 through 17.
    /// </param>
    /// <param name="probability">
    /// The probability (0.0 to 1.0) of the condition occurring at each applicable age.
    /// For example, 0.263 represents a 26.3% chance.
    /// </param>
    /// <param name="scenarioFactory">
    /// A factory function that creates a ScenarioBuilder to generate the clinical resources
    /// when the condition onsets. The factory receives the IFhirSchemaProvider.
    /// </param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="conditionName"/> or <paramref name="scenarioFactory"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="probability"/> is less than 0.0 or greater than 1.0.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Probabilistic conditions model real-world disease epidemiology. The condition
    /// is checked each year within the onset age range, and the probability is evaluated
    /// using a random number generator. Once a condition occurs, it will not trigger again.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// .AddProbabilisticCondition(
    ///     "Asthma",
    ///     onsetAges: 1..17,
    ///     probability: 0.263,
    ///     scenarioFactory: sp => new ScenarioBuilder(sp)
    ///         .AddConditionOnset(FhirCode.Conditions.Asthma)
    ///         .AddMedicationOrder(FhirCode.Medications.Albuterol))
    /// </code>
    /// </para>
    /// </remarks>
    public PatientLifecycleGenerator AddProbabilisticCondition(
        string conditionName,
        Range onsetAges,
        double probability,
        Func<IFhirSchemaProvider, ScenarioBuilder> scenarioFactory)
    {
        ArgumentNullException.ThrowIfNull(conditionName);
        ArgumentNullException.ThrowIfNull(scenarioFactory);

        if (probability < 0.0 || probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                probability,
                "Probability must be between 0.0 and 1.0.");
        }

        _events.Add(new ProbabilisticConditionOnset(conditionName, onsetAges, probability, scenarioFactory));
        return this;
    }

    /// <summary>
    /// Adds a custom lifecycle event to the simulation.
    /// </summary>
    /// <param name="lifecycleEvent">The lifecycle event to add.</param>
    /// <returns>The current generator instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="lifecycleEvent"/> is null.
    /// </exception>
    public PatientLifecycleGenerator AddEvent(ILifecycleEvent lifecycleEvent)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);
        _events.Add(lifecycleEvent);
        return this;
    }

    /// <summary>
    /// Simulates the patient's life from birth (age 0) to the specified target age,
    /// executing all applicable lifecycle events at each year of life.
    /// </summary>
    /// <param name="targetAge">
    /// The age (in years) to simulate until (inclusive). Must be non-negative.
    /// </param>
    /// <returns>
    /// A <see cref="ScenarioContext"/> containing the patient resource and all generated
    /// clinical resources (encounters, observations, conditions, medications, immunizations, etc.).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="targetAge"/> is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The simulation proceeds as follows:
    /// <list type="number">
    ///   <item><description>Create a new ScenarioContext</description></item>
    ///   <item><description>Generate the initial patient resource at age 0</description></item>
    ///   <item><description>For each age from 0 to targetAge:</description></item>
    ///   <item><description>  - Set context.CurrentTime to January 1st of that year of life</description></item>
    ///   <item><description>  - Execute all events where IsApplicable(age) returns true</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Events are executed in the order they were added to the generator. This allows
    /// dependent events (e.g., wellness visits that check for existing conditions) to
    /// operate on up-to-date context state.
    /// </para>
    /// </remarks>
    public ScenarioContext SimulateUntilAge(int targetAge)
    {
        if (targetAge < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetAge),
                targetAge,
                "Target age must be non-negative.");
        }

        // Create context and patient
        var context = new ScenarioContext
        {
            ScenarioName = $"Lifecycle_{_gender}_{_birthYear}",
            Description = $"Patient lifecycle simulation from birth to age {targetAge}"
        };

        // Generate initial patient at age 0
        var patient = GeneratePatient();
        context.Patient = patient;
        context.AddPatient(patient); // Add to AllResources for bundle generation
        context.BirthDate = new DateTime(_birthYear, 1, 1);
        context.CurrentTime = new DateTime(_birthYear, 1, 1);

        // Store demographics as attributes
        context.SetAttribute("gender", _gender);
        context.SetAttribute("birthYear", _birthYear);

        // Simulate year by year
        for (int age = 0; age <= targetAge; age++)
        {
            // Set current simulation date to January 1st of this year of life
            context.CurrentTime = new DateTime(_birthYear + age, 1, 1);
            context.SetAttribute("age", age);

            // Execute all applicable events for this age (in order added)
            foreach (var evt in _events)
            {
                if (evt.IsApplicable(age))
                {
                    evt.Execute(context, _schemaProvider);
                }
            }
        }

        return context;
    }

    /// <summary>
    /// Generates the initial patient resource with configured demographics.
    /// Delegates to PatientBuilder for consistent patient generation.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Patient resource.</returns>
    private ResourceJsonNode GeneratePatient()
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthYear(_birthYear)
            .WithGender(_gender);

        // Apply optional configured names
        if (_givenName is not null)
        {
            builder.WithGivenName(_givenName);
        }

        if (_familyName is not null)
        {
            builder.WithFamilyName(_familyName);
        }

        // Apply optional address (zip code triggers address generation in builder)
        if (!string.IsNullOrEmpty(_zipCode))
        {
            builder.WithZipCode(_zipCode);
        }

        // Apply optional phone (area code triggers telecom generation in builder)
        if (!string.IsNullOrEmpty(_areaCode))
        {
            builder.WithAreaCode(_areaCode);
        }

        return builder.Build();
    }
}
