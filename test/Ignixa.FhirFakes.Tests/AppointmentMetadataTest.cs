// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Quick test to verify Appointment.participant metadata.
/// </summary>
public class AppointmentMetadataTest
{
    private readonly ITestOutputHelper _output;

    public AppointmentMetadataTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenAppointment_WhenCheckingParticipant_ThenShouldBeRequired()
    {
        var schemaProvider = new STU3CoreSchemaProvider();
        var appointmentType = schemaProvider.GetTypeDefinition("Appointment");

        appointmentType.Should().NotBeNull();
        _output.WriteLine($"Appointment found with {appointmentType!.Children.Count} children");

        var participant = appointmentType.Children.FirstOrDefault(c => c.Info.Name == "participant");
        participant.Should().NotBeNull("participant element should exist");

        _output.WriteLine($"participant element:");
        _output.WriteLine($"  IsRequired: {participant!.IsRequired}");
        _output.WriteLine($"  IsCollection: {participant.IsCollection}");
        _output.WriteLine($"  InSummary: {participant.InSummary}");

        // Appointment.participant has min=1, so IsRequired should be true
        participant.IsRequired.Should().BeTrue("participant has min cardinality of 1");
        participant.IsCollection.Should().BeTrue("participant is an array");
    }
}
