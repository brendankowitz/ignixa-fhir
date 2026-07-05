// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow.Augmentors;

/// <summary>
/// Adds Appointment resources linking each (Patient, Encounter) subject to a rotating practitioner,
/// and back-references the appointment from Encounter.appointment. Configuration is fixed at
/// construction and <see cref="Augment"/> mutates no instance state, so one configured instance is
/// safe to reuse across calls.
/// </summary>
public sealed class AppointmentSchedulingAugmentor(
    IReadOnlyList<ResourceJsonNode> practitioners,
    IReadOnlyList<(ResourceJsonNode Patient, ResourceJsonNode Encounter)> appointmentSubjects,
    DateTimeOffset scheduleDate) : IResourceGraphAugmentor
{
    private const int SlotMinutes = 30;

    private static readonly string[] StatusRotation = ["booked", "booked", "booked", "fulfilled", "cancelled", "noshow"];

    private readonly IReadOnlyList<ResourceJsonNode> _practitioners = practitioners switch
    {
        null => throw new ArgumentNullException(nameof(practitioners)),
        { Count: 0 } => throw new ArgumentException("At least one practitioner is required.", nameof(practitioners)),
        _ => practitioners,
    };

    private readonly IReadOnlyList<(ResourceJsonNode Patient, ResourceJsonNode Encounter)> _appointmentSubjects =
        appointmentSubjects ?? throw new ArgumentNullException(nameof(appointmentSubjects));

    public void Augment(ResourceGraph graph, ResourceGraphAugmentationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        for (var i = 0; i < _appointmentSubjects.Count; i++)
        {
            var (patient, encounter) = _appointmentSubjects[i];
            var practitioner = _practitioners[i % _practitioners.Count];
            var status = StatusRotation[i % StatusRotation.Length];
            var start = scheduleDate.AddMinutes(i * SlotMinutes);
            var end = start.AddMinutes(SlotMinutes);

            var appointment = context.Faker.Generate("Appointment");
            var node = appointment.MutableNode;
            node["id"] = Guid.NewGuid().ToString();
            node["status"] = status;
            node["start"] = start.UtcDateTime.ToString("o");
            node["end"] = end.UtcDateTime.ToString("o");
            node["participant"] = new JsonArray
            {
                new JsonObject
                {
                    ["actor"] = new JsonObject { ["reference"] = $"Patient/{patient.Id}" },
                    ["status"] = "accepted",
                },
                new JsonObject
                {
                    ["actor"] = new JsonObject { ["reference"] = $"Practitioner/{practitioner.Id}" },
                    ["status"] = "accepted",
                },
            };

            graph.AddResource(appointment);

            var appointmentReference = new JsonObject { ["reference"] = $"Appointment/{appointment.Id}" };
            encounter.MutableNode["appointment"] = context.SchemaProvider.Version >= FhirVersion.R4
                ? new JsonArray { appointmentReference }
                : appointmentReference;
        }
    }
}
