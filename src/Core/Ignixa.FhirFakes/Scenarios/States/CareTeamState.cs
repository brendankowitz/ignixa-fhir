// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a CareTeam resource.
/// CareTeams coordinate care across multiple practitioners for a patient.
/// </summary>
/// <remarks>
/// This state generates FHIR CareTeam resources with:
/// <list type="bullet">
///   <item><description>Team name and status</description></item>
///   <item><description>Subject reference (patient)</description></item>
///   <item><description>Category codes for team classification</description></item>
///   <item><description>Participant members (practitioners, organizations, etc.)</description></item>
///   <item><description>Support for cross-references via StateId</description></item>
/// </list>
/// </remarks>
public sealed class CareTeamState : ScenarioState
{
    /// <summary>
    /// Gets the care team name.
    /// </summary>
    public required string TeamName { get; init; }

    /// <summary>
    /// Gets the care team status.
    /// Valid values: "proposed", "active", "suspended", "inactive", "entered-in-error".
    /// </summary>
    public string Status { get; init; } = "active";

    /// <summary>
    /// Gets the care team category code.
    /// If not specified, defaults to "clinical-research" (LA27976-2).
    /// </summary>
    public FhirCode? Category { get; init; }

    /// <summary>
    /// Gets the StateIds of practitioners to include as participants.
    /// References practitioners created with StateId in previous AddState() calls.
    /// </summary>
    public IReadOnlyList<string>? ParticipantStateIds { get; init; }

    /// <summary>
    /// Creates a CareTeam resource and stores it in the context.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create CareTeam without a Patient. Ensure WithPatient() is called first.");
        }

        var careTeam = faker.Generate("CareTeam");
        var node = careTeam.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;
        node["name"] = TeamName;

        // Set subject (patient reference)
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set category
        var categoryCode = Category ?? new FhirCode("http://loinc.org", "LA27976-2", "Clinical research");
        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = categoryCode.System,
                        ["code"] = categoryCode.Code,
                        ["display"] = categoryCode.Display
                    }
                }
            }
        };

        // Add participants by StateId
        if (ParticipantStateIds is not null && ParticipantStateIds.Count > 0)
        {
            var participantArray = new JsonArray();
            foreach (var stateId in ParticipantStateIds)
            {
                var practitioner = context.GetStateResource(stateId);
                if (practitioner is not null)
                {
                    participantArray.Add(new JsonObject
                    {
                        ["role"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["coding"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["system"] = "http://snomed.info/sct",
                                        ["code"] = "223366009",
                                        ["display"] = "Healthcare professional"
                                    }
                                }
                            }
                        },
                        ["member"] = new JsonObject
                        {
                            ["reference"] = $"Practitioner/{practitioner.Id}"
                        }
                    });
                }
            }

            node["participant"] = participantArray;
        }

        // Add to context
        context.AddCareTeam(careTeam, TeamName);

        // Register with StateId for cross-references
        context.RegisterStateResource(StateId, careTeam);
    }
}
