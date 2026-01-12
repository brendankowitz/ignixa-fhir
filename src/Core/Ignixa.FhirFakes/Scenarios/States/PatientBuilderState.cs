// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Initial state that creates the Patient resource using PatientBuilder.
/// This is typically the first state in any scenario and provides access to
/// sophisticated demographics through the PatientBuilder fluent API.
/// </summary>
public sealed class PatientBuilderState : ScenarioState
{
    private readonly Func<PatientBuilder, PatientBuilder> _configure;
    private readonly Func<IFhirSchemaProvider, PatientBuilder> _builderFactory;

    /// <summary>
    /// Gets or sets the starting simulation date. Defaults to 1 year ago.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// Creates a new PatientBuilderState with the specified configuration.
    /// </summary>
    /// <param name="configure">Configuration action for the PatientBuilder.</param>
    /// <param name="builderFactory">Factory function to create the PatientBuilder instance.</param>
    public PatientBuilderState(
        Func<PatientBuilder, PatientBuilder> configure,
        Func<IFhirSchemaProvider, PatientBuilder> builderFactory)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(builderFactory);

        _configure = configure;
        _builderFactory = builderFactory;
        Name = "PatientBuilder";
    }

    /// <summary>
    /// Creates the Patient resource using PatientBuilder and initializes the scenario context.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        // Create and configure the PatientBuilder
        var builder = _builderFactory(faker.SchemaProvider);
        builder = _configure(builder);

        // Apply the scenario's tag to the patient if set
        if (faker.Tag != null)
        {
            builder.WithTag(faker.Tag);
        }

        // Build the patient resource
        var patient = builder.Build();

        // Determine start date (default to 1 year ago)
        var startDate = StartDate ?? DateTime.UtcNow.AddYears(-1);

        // Calculate birth date from patient's configured age
        var age = builder.Age ?? 30; // Default age if not configured
        var birthDate = startDate.AddYears(-age);

        // Initialize context
        context.Patient = patient;
        context.BirthDate = birthDate;
        context.CurrentTime = startDate;

        // Add patient to AllResources collection so it's included in scenario.AllResources
        context.AddPatient(patient);

        // Store demographics as attributes for later use
        if (builder.Gender != null)
        {
            context.SetAttribute("gender", builder.Gender);
        }

        context.SetAttribute("age", age);

        if (builder.ProfileAttributes.TryGetValue(USCorePatientProfile.UsCoreRaceAttribute, out var ethnicity) && ethnicity is string ethnicityString)
        {
            context.SetAttribute("ethnicity", ethnicityString);
        }

        if (builder.ZipCode != null)
        {
            context.SetAttribute("zipCode", builder.ZipCode);
        }

        if (builder.BMI.HasValue)
        {
            context.SetAttribute("bmi", builder.BMI.Value);
        }
    }
}
