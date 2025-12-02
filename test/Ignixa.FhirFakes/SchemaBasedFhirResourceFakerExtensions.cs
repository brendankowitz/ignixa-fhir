// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes;

/// <summary>
/// Extension methods for <see cref="SchemaBasedFhirResourceFaker"/> providing convenient
/// resource creation shortcuts.
/// </summary>
public static class SchemaBasedFhirResourceFakerExtensions
{
    /// <summary>
    /// Creates a fake Patient resource.
    /// </summary>
    public static ResourceJsonNode CreatePatient(this SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return faker.Generate("Patient");
    }

    /// <summary>
    /// Creates a fake Observation resource.
    /// </summary>
    public static ResourceJsonNode CreateObservation(this SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return faker.Generate("Observation");
    }

    /// <summary>
    /// Creates a fake Condition resource.
    /// </summary>
    public static ResourceJsonNode CreateCondition(this SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return faker.Generate("Condition");
    }

    /// <summary>
    /// Creates a fake Encounter resource.
    /// </summary>
    public static ResourceJsonNode CreateEncounter(this SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return faker.Generate("Encounter");
    }

    /// <summary>
    /// Creates a fake MedicationRequest resource.
    /// </summary>
    public static ResourceJsonNode CreateMedicationRequest(this SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return faker.Generate("MedicationRequest");
    }

    /// <summary>
    /// Creates a transaction bundle containing a patient compartment with related resources.
    /// </summary>
    /// <param name="faker">The faker instance.</param>
    /// <param name="observationCount">Number of observations to create (default: 2).</param>
    /// <param name="conditionCount">Number of conditions to create (default: 1).</param>
    /// <param name="encounterCount">Number of encounters to create (default: 1).</param>
    /// <returns>A BundleJsonNode containing all the resources.</returns>
    public static BundleJsonNode CreatePatientCompartmentBundle(
        this SchemaBasedFhirResourceFaker faker,
        int observationCount = 2,
        int conditionCount = 1,
        int encounterCount = 1)
    {
        ArgumentNullException.ThrowIfNull(faker);

        var patient = faker.CreatePatient();
        var patientId = patient.Id;
        var patientReference = $"Patient/{patientId}";

        var entries = new JsonArray();

        // Add patient entry
        entries.Add(CreateBundleEntry(patient, "POST", "Patient"));

        // Add encounters
        for (int i = 0; i < encounterCount; i++)
        {
            var encounter = faker.CreateEncounter();
            encounter.MutableNode["subject"] = new JsonObject
            {
                ["reference"] = patientReference
            };
            entries.Add(CreateBundleEntry(encounter, "POST", "Encounter"));
        }

        // Add conditions
        for (int i = 0; i < conditionCount; i++)
        {
            var condition = faker.CreateCondition();
            condition.MutableNode["subject"] = new JsonObject
            {
                ["reference"] = patientReference
            };
            entries.Add(CreateBundleEntry(condition, "POST", "Condition"));
        }

        // Add observations
        for (int i = 0; i < observationCount; i++)
        {
            var observation = faker.CreateObservation();
            observation.MutableNode["subject"] = new JsonObject
            {
                ["reference"] = patientReference
            };
            entries.Add(CreateBundleEntry(observation, "POST", "Observation"));
        }

        // Create the bundle
        var bundleNode = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "transaction",
            ["entry"] = entries
        };

        return new BundleJsonNode(bundleNode);
    }

    private static JsonObject CreateBundleEntry(ResourceJsonNode resource, string method, string resourceType)
    {
        return new JsonObject
        {
            ["fullUrl"] = $"urn:uuid:{resource.Id}",
            ["resource"] = resource.MutableNode,
            ["request"] = new JsonObject
            {
                ["method"] = method,
                ["url"] = resourceType
            }
        };
    }
}
