// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class StructureDefinitionFacadeTests
{
    [Fact]
    public void GivenStructureDefinition_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var structureDefinition = new StructureDefinition
        {
            Url = "http://example.org/fhir/StructureDefinition/test",
            Name = "TestProfile",
            Type = "Patient",
            Kind = StructureDefinitionKind.Resource,
            Derivation = TypeDerivationRule.Constraint,
        };

        structureDefinition.Url.ShouldBe("http://example.org/fhir/StructureDefinition/test");
        structureDefinition.Name.ShouldBe("TestProfile");
        structureDefinition.Type.ShouldBe("Patient");
        structureDefinition.Kind.ShouldBe(StructureDefinitionKind.Resource);
        structureDefinition.Derivation.ShouldBe(TypeDerivationRule.Constraint);
    }

    [Fact]
    public void GivenCustomResourceStructureDefinition_WhenKindAndDerivationChecked_ThenMatchesSpecializationPattern()
    {
        var structureDefinition = new StructureDefinition
        {
            Kind = StructureDefinitionKind.Resource,
            Derivation = TypeDerivationRule.Specialization,
        };

        (structureDefinition.Kind == StructureDefinitionKind.Resource
            && structureDefinition.Derivation == TypeDerivationRule.Specialization).ShouldBeTrue();
    }

    [Fact]
    public void GivenLogicalModelStructureDefinition_WhenKindChecked_ThenMatchesLogicalPattern()
    {
        var structureDefinition = new StructureDefinition
        {
            Kind = StructureDefinitionKind.Logical,
        };

        structureDefinition.Kind.ShouldBe(StructureDefinitionKind.Logical);
    }
}
