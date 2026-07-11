// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

/// <summary>
/// <c>Observation.value[x]</c>: only one suffixed variant key is present at a time. These tests
/// pin per-variant accessors, the discriminator, and the clear-siblings setter semantics that the
/// runtime-agreement tests do not exercise (they only read variants; these mutate them).
///
/// Ported from the typed-models spike (ChoiceTypeSpikeTests) against the graduated generated types.
/// </summary>
public sealed class ChoiceTypeTests
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

    private const string ObservationStringWithShadowJson =
        """
        {
          "resourceType": "Observation",
          "id": "obs-s",
          "status": "final",
          "valueString": "borderline",
          "_valueString": { "extension": [ { "url": "http://example.org/note", "valueString": "flagged" } ] }
        }
        """;

    [Fact]
    public void GivenValueQuantity_WhenRead_ThenVariantAndDiscriminatorReportQuantity()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.Quantity);
        obs.ValueQuantity.ShouldNotBeNull();
        obs.ValueQuantity!.Value.ShouldBe(185);
        obs.ValueQuantity.Unit.ShouldBe("cm");
        obs.ValueString.ShouldBeNull();
    }

    [Fact]
    public void GivenValueString_WhenRead_ThenVariantAndDiscriminatorReportString()
    {
        var obs = ResourceJsonNode.Parse(ObservationStringJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.String);
        obs.ValueString.ShouldBe("borderline");
        obs.ValueQuantity.ShouldBeNull();
    }

    [Fact]
    public void GivenValueQuantity_WhenSettingValueString_ThenQuantityKeyRemoved()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueString = "switched";

        obs.MutableNode()["valueQuantity"].ShouldBeNull();
        obs.MutableNode()["valueString"]!.GetValue<string>().ShouldBe("switched");
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.String);
    }

    [Fact]
    public void GivenNoValue_WhenChecked_ThenDiscriminatorIsNone()
    {
        var obs = ResourceJsonNode.Parse(
            """{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();

        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.None);
        obs.Value.ShouldBeNull();
    }

    [Fact]
    public void GivenActiveValueVariant_WhenSetToNull_ThenKeyRemovedAndDiscriminatorIsNone()
    {
        // Clearing the active typed variant (set it to null) must remove the JSON key entirely, drop
        // the discriminator back to None, and leave no raw Value node -- the choice becomes absent.
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<Ignixa.Models.R4.Observation>();
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.Quantity);

        obs.ValueQuantity = null;

        obs.MutableNode()["valueQuantity"].ShouldBeNull();
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.None);
        obs.Value.ShouldBeNull();
        obs.ValueQuantity.ShouldBeNull();
    }

    [Fact]
    public void GivenValueQuantity_WhenSettingCodeableConcept_ThenOnlyOneVariantRemains()
    {
        var obs = ResourceJsonNode.Parse(ObservationQuantityJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueCodeableConcept = new Ignixa.Models.CodeableConcept { Text = "elevated" };

        obs.MutableNode()["valueQuantity"].ShouldBeNull();
        obs.MutableNode()["valueString"].ShouldBeNull();
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.CodeableConcept);
        obs.ValueCodeableConcept!.Text.ShouldBe("elevated");
    }

    [Fact]
    public void GivenValueStringWithPrimitiveExtensionShadow_WhenSwitchingToValueQuantity_ThenShadowCompanionRemoved()
    {
        // valueString's sibling "_valueString" carries a primitive extension (FHIR's underscore-prefixed
        // primitive-extension mechanism, https://hl7.org/fhir/json.html#primitive). Switching variants must
        // clear that companion too, or it survives as an orphan with no primitive value to annotate --
        // invalid FHIR JSON. Regression test for issue #334.
        var obs = ResourceJsonNode.Parse(ObservationStringWithShadowJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueQuantity = new Ignixa.Models.R4.Quantity { Value = 185 };

        obs.MutableNode()["valueString"].ShouldBeNull();
        obs.MutableNode()["_valueString"].ShouldBeNull();
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.Quantity);
    }

    [Fact]
    public void GivenValueStringWithPrimitiveExtensionShadow_WhenSetToNull_ThenShadowPreserved()
    {
        // Clearing a choice variant's value while leaving its extension shadow in place is valid FHIR (an
        // extension-only primitive with no value). Setting the variant to null must remove only the value
        // key, never its "_"-prefixed companion.
        var obs = ResourceJsonNode.Parse(ObservationStringWithShadowJson).As<Ignixa.Models.R4.Observation>();

        obs.ValueString = null;

        obs.MutableNode()["valueString"].ShouldBeNull();
        obs.MutableNode()["_valueString"].ShouldNotBeNull();
        obs.ValueType.ShouldBe(Ignixa.Models.R4.ObservationValueType.None);
    }
}
