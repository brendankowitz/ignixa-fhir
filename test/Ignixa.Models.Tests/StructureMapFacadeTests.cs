// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.Extensions;
using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class StructureMapFacadeTests
{
    [Fact]
    public void GivenStructureMap_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var map = new StructureMap
        {
            Url = "http://example.org/fhir/StructureMap/test",
            Name = "TestMap",
            Status = PublicationStatus.Active,
        };
        map.Structure.Add(new StructureMapStructure { Url = "http://hl7.org/fhir/StructureDefinition/Patient" });
        map.Group.Add(new StructureMapGroup { Name = "main" });

        map.Url.ShouldBe("http://example.org/fhir/StructureMap/test");
        map.Status.ShouldBe(PublicationStatus.Active);
        map.Structure.Single().Url.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
        map.Group.Single().Name.ShouldBe("main");
    }

    [Fact]
    public void GivenR4GroupAndR5Group_WhenTypeModeSet_ThenEachUsesItsOwnEnum()
    {
        var r4Group = new Ignixa.Models.R4.StructureMapGroup { TypeMode = Ignixa.Models.R4.MapGroupTypeMode.None };
        var r5Group = new Ignixa.Models.R5.StructureMapGroup { TypeMode = Ignixa.Models.R5.MapGroupTypeMode.Types };

        // R5's map-group-type-mode value set dropped "none" entirely -- confirms the two are genuinely
        // different enums, not just differently-visible views of one shared type.
        r4Group.TypeMode.ShouldBe(Ignixa.Models.R4.MapGroupTypeMode.None);
        r5Group.TypeMode.ShouldBe(Ignixa.Models.R5.MapGroupTypeMode.Types);
    }

    [Fact]
    public void GivenR4Dependent_WhenUsingGetAndAddDependentVariable_ThenUsesVariableArray()
    {
        var dependent = new StructureMapGroupRuleDependent(new JsonObject(), FhirVersion.R4);

        dependent.AddDependentVariable("var1");
        dependent.AddDependentVariable("var2");

        dependent.MutableNode().ContainsKey("variable").ShouldBeTrue();
        dependent.MutableNode().ContainsKey("parameter").ShouldBeFalse();
        dependent.GetDependentVariables().ShouldBe(["var1", "var2"]);
    }

    [Fact]
    public void GivenR5Dependent_WhenUsingGetAndAddDependentVariable_ThenUsesParameterArray()
    {
        var dependent = new StructureMapGroupRuleDependent(new JsonObject(), FhirVersion.R5);

        dependent.AddDependentVariable("var1");

        dependent.MutableNode().ContainsKey("parameter").ShouldBeTrue();
        dependent.MutableNode().ContainsKey("variable").ShouldBeFalse();
        dependent.GetDependentVariables().ShouldBe(["var1"]);
    }

    [Fact]
    public void GivenR4Source_WhenUsingSetDefaultValueString_ThenSetsDefaultValueStringVariant()
    {
        var source = new StructureMapGroupRuleSource(new JsonObject(), FhirVersion.R4);

        source.SetDefaultValueString("test value");

        source.MutableNode().ContainsKey("defaultValueString").ShouldBeTrue();
        source.GetDefaultValueString().ShouldBe("test value");
    }

    [Fact]
    public void GivenR5Source_WhenUsingSetDefaultValueString_ThenSetsPlainDefaultValue()
    {
        var source = new StructureMapGroupRuleSource(new JsonObject(), FhirVersion.R5);

        source.SetDefaultValueString("test value");

        source.MutableNode().ContainsKey("defaultValue").ShouldBeTrue();
        source.GetDefaultValueString().ShouldBe("test value");
    }

    [Fact]
    public void GivenR4SourceWithTypedDefaultValue_WhenRoundTripping_ThenPreservesType()
    {
        var source = new StructureMapGroupRuleSource(new JsonObject(), FhirVersion.R4);
        source.MutableNode()["defaultValueInteger"] = 42;

        var json = source.MutableNode().ToJsonString();
        var roundTripped = new StructureMapGroupRuleSource((JsonObject)JsonNode.Parse(json)!, FhirVersion.R4);

        roundTripped.GetDefaultValue().ShouldNotBeNull();
        roundTripped.GetDefaultValue()!.GetValue<int>().ShouldBe(42);
        roundTripped.MutableNode().ContainsKey("defaultValueInteger").ShouldBeTrue();
    }

    [Fact]
    public void GivenTarget_WhenContextSet_ThenRoundTrips()
    {
        var target = new StructureMapGroupRuleTarget();

        target.SetContext("tgt");

        target.GetContext().ShouldBe("tgt");
    }

    [Fact]
    public void GivenTargetParameter_WhenValueSet_ThenClearsOtherVariantsFirst()
    {
        var parameter = new StructureMapGroupRuleTargetParameter();

        parameter.SetValue("String", JsonValue.Create("first"));
        parameter.SetValue("Integer", JsonValue.Create(42));

        parameter.MutableNode().ContainsKey("valueString").ShouldBeFalse();
        parameter.GetValueAs<int>().ShouldBe(42);
    }

    [Fact]
    public void GivenR5Map_WhenCheckingSupportsConstants_ThenReturnsTrue()
    {
        var map = new StructureMap { FhirVersion = FhirVersion.R5 };

        map.SupportsConstants().ShouldBeTrue();
    }

    [Fact]
    public void GivenR4Map_WhenCheckingSupportsConstantsAndGettingConstants_ThenReturnsFalseAndEmpty()
    {
        var map = new StructureMap { FhirVersion = FhirVersion.R4 };

        map.SupportsConstants().ShouldBeFalse();
        map.GetConstantsOrEmpty().ShouldBeEmpty();
    }
}
