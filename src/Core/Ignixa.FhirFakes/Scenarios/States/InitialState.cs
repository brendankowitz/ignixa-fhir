// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Initial state that creates the Patient resource with demographics.
/// This is typically the first state in any scenario.
/// </summary>
public sealed class InitialState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the patient's age in years. If null, a random age is generated.
    /// </summary>
    public int? Age { get; init; }

    /// <summary>
    /// Gets or sets the patient's gender ("male", "female", "other", "unknown").
    /// </summary>
    public string? Gender { get; init; }

    /// <summary>
    /// Gets or sets the patient's given name. If null, a random name is generated.
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Gets or sets the patient's family name. If null, a random name is generated.
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Gets or sets the starting simulation date. Defaults to 1 year ago.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// Creates the Patient resource and initializes the scenario context.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        // Determine demographics
        var gender = Gender ?? _faker.PickRandom("male", "female");
        var givenName = GivenName ?? (gender == "male" ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male) : _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female));
        var familyName = FamilyName ?? _faker.Name.LastName();
        var age = Age ?? _faker.Random.Int(25, 70);

        // Calculate birth date from age
        var startDate = StartDate ?? DateTime.UtcNow.AddYears(-1);
        var birthDate = startDate.AddYears(-age);

        // Create patient resource using the faker as base, then customize
        var patient = faker.Generate("Patient");
        var patientNode = patient.MutableNode;

        // Set specific values
        patientNode["id"] = Guid.NewGuid().ToString();
        patientNode["gender"] = gender;
        patientNode["birthDate"] = birthDate.ToString("yyyy-MM-dd");

        // Set name
        var nameArray = new JsonArray
        {
            new JsonObject
            {
                ["use"] = "official",
                ["family"] = familyName,
                ["given"] = new JsonArray(JsonValue.Create(givenName))
            }
        };
        patientNode["name"] = nameArray;

        // Initialize context
        context.Patient = patient;
        context.AddPatient(patient);  // Add patient to AllResources
        context.BirthDate = birthDate;
        context.CurrentTime = startDate;

        // Store demographics as attributes for later use
        context.SetAttribute("gender", gender);
        context.SetAttribute("age", age);
    }
}
