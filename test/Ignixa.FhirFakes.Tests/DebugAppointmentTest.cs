// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

public class DebugAppointmentTest
{
    private readonly ITestOutputHelper _output;

    public DebugAppointmentTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DebugAppointmentGeneration()
    {
        var schemaProvider = new STU3CoreSchemaProvider();

        // Check schema first
        var appointmentType = schemaProvider.GetTypeDefinition("Appointment");
        appointmentType.Should().NotBeNull();

        var participantElement = appointmentType!.Children.FirstOrDefault(c => c.Info.Name == "participant");
        participantElement.Should().NotBeNull();

        _output.WriteLine("Schema info:");
        _output.WriteLine($"  participant.IsRequired: {participantElement!.IsRequired}");
        _output.WriteLine($"  participant.IsCollection: {participantElement.IsCollection}");
        _output.WriteLine($"  participant has {participantElement.Children.Count} children");

        if (participantElement is Ignixa.Abstractions.ITypeExtended extended)
        {
            _output.WriteLine($"  participant Types.Count: {extended.Types.Count}");
            if (extended.Types.Count > 0)
            {
                _output.WriteLine($"  participant Type[0].Code: {extended.Types[0].Code}");

                // Try to get the BackboneElement type definition
                var participantTypeName = $"Appointment.participant";
                var participantTypeDef = schemaProvider.GetTypeDefinition(participantTypeName);
                if (participantTypeDef != null)
                {
                    _output.WriteLine($"  Found type definition for '{participantTypeName}' with {participantTypeDef.Children.Count} children");
                }
                else
                {
                    _output.WriteLine($"  Could not find type definition for '{participantTypeName}'");
                }
            }
        }

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        var appointment = faker.Generate("Appointment");
        var json = appointment.MutableNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        _output.WriteLine("\nGenerated Appointment:");
        _output.WriteLine(json);

        // Check if participant exists
        var hasParticipant = appointment.MutableNode.AsObject().ContainsKey("participant");
        _output.WriteLine($"\nHas participant: {hasParticipant}");

        if (hasParticipant)
        {
            var participant = appointment.MutableNode["participant"];
            _output.WriteLine($"Participant value: {participant}");
        }
    }
}
