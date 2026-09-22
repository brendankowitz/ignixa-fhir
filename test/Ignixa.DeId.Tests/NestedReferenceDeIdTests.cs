// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.DeId.Tests.Fixtures;

namespace Ignixa.DeId.Tests;

/// <summary>
/// Falsification tests for issue #454's de-identification impact.
/// </summary>
/// <remarks>
/// De-identification rules select references by type, not by path - the shipped configurations use
/// <c>descendants().ofType(Reference).reference</c> for hashing and
/// <c>descendants().ofType(Reference).display</c> for redaction, and both bootstrap policies
/// (Safe Harbor and Expert Determination) carry the <c>display</c> rule. <c>ofType</c> resolves on
/// <c>InstanceType</c>, so while the element model reported <c>Encounter.Location</c> for
/// <c>Encounter.location.location</c> instead of <c>Reference</c>, neither rule matched: the
/// identifier passed through un-hashed and the display text - routinely a facility or person name -
/// passed through un-redacted, in output the caller believes is de-identified.
/// <para>
/// This is a different severity from the empty search bundle #454 is filed for, and it is why these
/// are pinned by name rather than left to the <c>Account</c> snapshot fixture, which covers the
/// hashing half only and records it as two changed hash strings.
/// </para>
/// </remarks>
[Collection("DeId Engine Collection")]
public class NestedReferenceDeIdTests
{
    private readonly DeIdEngineFixture _fixture;

    public NestedReferenceDeIdTests(DeIdEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAReferenceNestedUnderASameNamedBackbone_WhenDeidentified_ThenItIsHashedAndItsDisplayRedacted()
    {
        // Arrange: Encounter.location.location - a Reference whose parent backbone shares its name.
        var encounterJson =
            """
            {
              "resourceType": "Encounter",
              "id": "enc1",
              "status": "in-progress",
              "class": { "code": "AMB" },
              "location": [
                {
                  "location": { "reference": "Location/ward-4b", "display": "Ward 4B, St Elsewhere" },
                  "status": "active"
                }
              ]
            }
            """;

        // Act
        var result = await _fixture.R4ConfigurationSampleEngine.DeidentifyAsync(encounterJson);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        var nested = JsonNode.Parse(result.Value.DeidentifiedJson)!["location"]![0]!["location"]!;

        var reference = nested["reference"]?.GetValue<string>();
        reference.ShouldNotBeNull();
        reference.ShouldNotBe("Location/ward-4b", "the nested reference must not survive de-identification unchanged");
        reference.ShouldStartWith("Location/");

        // Redaction removes the property outright; the assertion is that the facility name is gone
        // whatever shape redaction takes, not that a particular placeholder was written.
        nested["display"]?.GetValue<string>().ShouldNotBe("Ward 4B, St Elsewhere");
    }

    /// <summary>
    /// The control: a reference in the ordinary position was always hashed, so a test that only
    /// checked one of these could pass on machinery that never reached the nested node.
    /// </summary>
    [Fact]
    public async Task GivenAReferenceInAnOrdinaryPosition_WhenDeidentified_ThenItIsStillHashed()
    {
        var encounterJson =
            """
            {
              "resourceType": "Encounter",
              "id": "enc2",
              "status": "in-progress",
              "class": { "code": "AMB" },
              "subject": { "reference": "Patient/p1", "display": "Jane Roe" }
            }
            """;

        var result = await _fixture.R4ConfigurationSampleEngine.DeidentifyAsync(encounterJson);

        result.IsSuccess.ShouldBeTrue();

        var subject = JsonNode.Parse(result.Value.DeidentifiedJson)!["subject"]!;
        subject["reference"]!.GetValue<string>().ShouldNotBe("Patient/p1");
        subject["display"]?.GetValue<string>().ShouldNotBe("Jane Roe");
    }
}
