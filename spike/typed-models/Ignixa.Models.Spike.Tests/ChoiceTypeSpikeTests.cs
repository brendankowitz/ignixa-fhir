// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;
using R4 = Ignixa.Models.R4;

namespace Ignixa.Models.Spike.Tests;

/// <summary>
/// Hard part 2: <c>Observation.value[x]</c>. Only one suffixed variant key is present at a time.
/// These tests prove per-variant accessors, the discriminator, and clear-siblings setter semantics.
/// </summary>
public sealed class ChoiceTypeSpikeTests
{
    private const string ObservationQuantityJson =
        """
        {
          "resourceType": "Observation",
          "id": "obs-q",
          "status": "final",
          "valueQuantity": { "value": 185, "unit": "cm", "code": "cm" }
        }
        """;

    private const string ObservationStringJson =
        """
        {
          "resourceType": "Observation",
          "id": "obs-s",
          "status": "final",
          "valueString": "borderline"
        }
        """;

    [Fact]
    public void GivenValueQuantity_WhenRead_ThenVariantAndDiscriminatorReportQuantity()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<R4.Observation>();

        obs.ValueType.ShouldBe(R4.ObservationValueType.Quantity);
        obs.ValueQuantity.ShouldNotBeNull();
        obs.ValueQuantity!.Value.ShouldBe(185);
        obs.ValueQuantity.Unit.ShouldBe("cm");
        obs.ValueString.ShouldBeNull();
    }

    [Fact]
    public void GivenValueString_WhenRead_ThenVariantAndDiscriminatorReportString()
    {
        var obs = ResourceJsonNode.Parse(ObservationStringJson).As<R4.Observation>();

        obs.ValueType.ShouldBe(R4.ObservationValueType.String);
        obs.ValueString.ShouldBe("borderline");
        obs.ValueQuantity.ShouldBeNull();
    }

    [Fact]
    public void GivenValueQuantity_WhenSettingValueString_ThenQuantityKeyRemoved()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<R4.Observation>();

        obs.ValueString = "switched";

        obs.MutableNode["valueQuantity"].ShouldBeNull();
        obs.MutableNode["valueString"]!.GetValue<string>().ShouldBe("switched");
        obs.ValueType.ShouldBe(R4.ObservationValueType.String);
    }

    [Fact]
    public void GivenNoValue_WhenChecked_ThenDiscriminatorIsNone()
    {
        var obs = ResourceJsonNode.Parse(
            """{ "resourceType": "Observation", "status": "final" }""").As<R4.Observation>();

        obs.ValueType.ShouldBe(R4.ObservationValueType.None);
        obs.Value.ShouldBeNull();
    }

    [Fact]
    public void GivenValueQuantity_WhenSettingCodeableConcept_ThenOnlyOneVariantRemains()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<R4.Observation>();

        obs.ValueCodeableConcept = new R4.CodeableConcept { Text = "elevated" };

        obs.MutableNode["valueQuantity"].ShouldBeNull();
        obs.MutableNode["valueString"].ShouldBeNull();
        obs.ValueType.ShouldBe(R4.ObservationValueType.CodeableConcept);
        obs.ValueCodeableConcept!.Text.ShouldBe("elevated");
    }
}
