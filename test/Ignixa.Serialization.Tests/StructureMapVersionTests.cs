// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Ignixa.Serialization.Models;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests for version-aware StructureMap properties and validation.
/// Ensures R4-only properties throw in R5 context and vice versa.
/// </summary>
public class StructureMapVersionTests
{
    #region R5+ Properties (should throw in R4)

    [Fact]
    public void GivenR4StructureMap_WhenAccessingConst_ThenThrowsNotSupportedException()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R4;

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => map.Const.Add(new StructureMapConstJsonNode()));
        exception.Message.Should().Contain("Const is not supported in R4");
        exception.Message.Should().Contain("introduced in FHIR R5");
    }

    [Fact]
    public void GivenR4StructureMap_WhenAccessingVersionAlgorithmString_ThenThrowsNotSupportedException()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R4;

        // Act & Assert
        var getException = Assert.Throws<NotSupportedException>(() => _ = map.VersionAlgorithmString);
        getException.Message.Should().Contain("VersionAlgorithmString is not supported in R4");

        var setException = Assert.Throws<NotSupportedException>(() => map.VersionAlgorithmString = "semver");
        setException.Message.Should().Contain("VersionAlgorithmString is not supported in R4");
    }

    [Fact]
    public void GivenR4StructureMap_WhenAccessingCopyrightLabel_ThenThrowsNotSupportedException()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R4;

        // Act & Assert
        var getException = Assert.Throws<NotSupportedException>(() => _ = map.CopyrightLabel);
        getException.Message.Should().Contain("CopyrightLabel is not supported in R4");

        var setException = Assert.Throws<NotSupportedException>(() => map.CopyrightLabel = "© 2025");
        setException.Message.Should().Contain("CopyrightLabel is not supported in R4");
    }

    [Fact]
    public void GivenR5StructureMap_WhenAccessingConst_ThenSucceeds()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R5;

        // Act
        var constNode = new StructureMapConstJsonNode(new JsonObject(), FhirVersion.R5)
        {
            Name = "myConstant",
            Value = "'some value'"
        };
        map.Const.Add(constNode);

        // Assert
        map.Const.Should().HaveCount(1);
        map.Const.First().Name.Should().Be("myConstant");
    }

    #endregion

    #region R4 Properties (should throw in R5)

    [Fact]
    public void GivenR5Dependent_WhenAccessingVariable_ThenThrowsNotSupportedException()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R5);

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => dependent.Variable.Add("var1"));
        exception.Message.Should().Contain("Variable is not supported in R5");
        exception.Message.Should().Contain("use the Parameter property instead");
    }

    [Fact]
    public void GivenR4Dependent_WhenAccessingParameter_ThenThrowsNotSupportedException()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R4);

        // Act & Assert
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R4);
        var exception = Assert.Throws<NotSupportedException>(() => dependent.Parameter.Add(param));
        exception.Message.Should().Contain("Parameter is not supported in R4");
        exception.Message.Should().Contain("use the Variable property instead");
    }

    [Fact]
    public void GivenR4Dependent_WhenAccessingVariable_ThenSucceeds()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R4)
        {
            Name = "callGroup"
        };

        // Act
        dependent.Variable.Add("var1");
        dependent.Variable.Add("var2");

        // Assert
        dependent.Variable.Should().HaveCount(2);
        dependent.Variable.Should().Contain("var1");
        dependent.Variable.Should().Contain("var2");
    }

    [Fact]
    public void GivenR5Dependent_WhenAccessingParameter_ThenSucceeds()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R5)
        {
            Name = "callGroup"
        };

        // Act
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R5);
        param.SetValue("String", JsonValue.Create("value1"));
        dependent.Parameter.Add(param);

        // Assert
        dependent.Parameter.Should().HaveCount(1);
        dependent.Parameter.First().GetValueAs<string>().Should().Be("value1");
    }

    #endregion

    #region DefaultValue R4 vs R5

    [Fact]
    public void GivenR5Source_WhenAccessingDefaultValueProperty_ThenSucceeds()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R5);

        // Act
        source.DefaultValue = "'some FHIRPath expression'";

        // Assert
        source.DefaultValue.Should().Be("'some FHIRPath expression'");
    }

    [Fact]
    public void GivenR4Source_WhenAccessingDefaultValueProperty_ThenThrowsNotSupportedException()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R4);

        // Act & Assert
        var getException = Assert.Throws<NotSupportedException>(() => _ = source.DefaultValue);
        getException.Message.Should().Contain("DefaultValue (string) is not supported in R4");

        var setException = Assert.Throws<NotSupportedException>(() => source.DefaultValue = "value");
        setException.Message.Should().Contain("DefaultValue (string) is not supported in R4");
    }

    [Fact]
    public void GivenR4Source_WhenUsingSetDefaultValue_ThenSucceeds()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R4);

        // Act
        source.SetDefaultValue("String", JsonValue.Create("test"));

        // Assert
        var defaultValue = source.GetDefaultValue();
        defaultValue.Should().NotBeNull();
        defaultValue!.GetValue<string>().Should().Be("test");
    }

    [Fact]
    public void GivenR5Source_WhenUsingSetDefaultValue_ThenThrowsNotSupportedException()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R5);

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            source.SetDefaultValue("String", JsonValue.Create("test")));
        exception.Message.Should().Contain("SetDefaultValue(suffix, value) is not supported in R5");
    }

    #endregion

    #region Parameter Type Validation

    [Fact]
    public void GivenR5Parameter_WhenSettingDateValue_ThenSucceeds()
    {
        // Arrange
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R5);

        // Act
        param.SetValueDate("2025-01-15");

        // Assert
        var value = param.GetValue();
        value.Should().NotBeNull();
        value!.GetValue<string>().Should().Be("2025-01-15");
    }

    [Fact]
    public void GivenR4Parameter_WhenSettingDateValue_ThenThrowsNotSupportedException()
    {
        // Arrange
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R4);

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => param.SetValueDate("2025-01-15"));
        exception.Message.Should().Contain("valueDate is not supported in R4");
        exception.Message.Should().Contain("id, string, boolean, integer, decimal");
    }

    [Fact]
    public void GivenR4Parameter_WhenSettingTimeValue_ThenThrowsNotSupportedException()
    {
        // Arrange
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R4);

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => param.SetValueTime("14:30:00"));
        exception.Message.Should().Contain("valueTime is not supported in R4");
    }

    [Fact]
    public void GivenR4Parameter_WhenSettingDateTimeValue_ThenThrowsNotSupportedException()
    {
        // Arrange
        var param = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R4);

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => param.SetValueDateTime("2025-01-15T14:30:00Z"));
        exception.Message.Should().Contain("valueDateTime is not supported in R4");
    }

    #endregion

    #region Required Field Validation

    [Fact]
    public void GivenR4Group_WhenSettingTypeModeToNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var group = new StructureMapGroupJsonNode(new JsonObject(), FhirVersion.R4)
        {
            Name = "myGroup",
            TypeMode = StructureMapGroupTypeMode.None
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => group.TypeMode = null);
        exception.Message.Should().Contain("TypeMode is required in R4");
        exception.Message.Should().Contain("In R5+, this field became optional");
    }

    [Fact]
    public void GivenR5Group_WhenSettingTypeModeToNull_ThenSucceeds()
    {
        // Arrange
        var group = new StructureMapGroupJsonNode(new JsonObject(), FhirVersion.R5)
        {
            Name = "myGroup",
            TypeMode = StructureMapGroupTypeMode.None
        };

        // Act
        group.TypeMode = null;

        // Assert
        group.TypeMode.Should().BeNull();
    }

    [Fact]
    public void GivenR4Rule_WhenSettingNameToNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var rule = new StructureMapRuleJsonNode(new JsonObject(), FhirVersion.R4)
        {
            Name = "myRule"
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => rule.Name = null);
        exception.Message.Should().Contain("Name is required in R4");
    }

    [Fact]
    public void GivenR5Rule_WhenSettingNameToNull_ThenSucceeds()
    {
        // Arrange
        var rule = new StructureMapRuleJsonNode(new JsonObject(), FhirVersion.R5)
        {
            Name = "myRule"
        };

        // Act
        rule.Name = null;

        // Assert
        rule.Name.Should().BeNull();
    }

    #endregion

    #region Extension Methods

    [Fact]
    public void GivenR4Dependent_WhenUsingGetDependentVariables_ThenReturnsVariables()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R4);
        dependent.Variable.Add("var1");
        dependent.Variable.Add("var2");

        // Act
        var variables = dependent.GetDependentVariables().ToList();

        // Assert
        variables.Should().HaveCount(2);
        variables.Should().Contain("var1");
        variables.Should().Contain("var2");
    }

    [Fact]
    public void GivenR5Dependent_WhenUsingGetDependentVariables_ThenReturnsParameterValues()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R5);
        var param1 = new StructureMapParameterJsonNode(new JsonObject(), FhirVersion.R5);
        param1.SetValue("String", JsonValue.Create("var1"));
        dependent.Parameter.Add(param1);

        // Act
        var variables = dependent.GetDependentVariables().ToList();

        // Assert
        variables.Should().HaveCount(1);
        variables.Should().Contain("var1");
    }

    [Fact]
    public void GivenR4Dependent_WhenUsingAddDependentVariable_ThenAddsToVariable()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R4);

        // Act
        dependent.AddDependentVariable("var1");

        // Assert
        dependent.Variable.Should().HaveCount(1);
        dependent.Variable.Should().Contain("var1");
    }

    [Fact]
    public void GivenR5Dependent_WhenUsingAddDependentVariable_ThenAddsToParameter()
    {
        // Arrange
        var dependent = new StructureMapDependentJsonNode(new JsonObject(), FhirVersion.R5);

        // Act
        dependent.AddDependentVariable("var1");

        // Assert
        dependent.Parameter.Should().HaveCount(1);
        dependent.Parameter.First().GetValueAs<string>().Should().Be("var1");
    }

    [Fact]
    public void GivenR5Source_WhenUsingSetDefaultValueString_ThenSetsDefaultValue()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R5);

        // Act
        source.SetDefaultValueString("test value");

        // Assert
        source.DefaultValue.Should().Be("test value");
    }

    [Fact]
    public void GivenR4Source_WhenUsingSetDefaultValueString_ThenSetsDefaultValueString()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R4);

        // Act
        source.SetDefaultValueString("test value");

        // Assert
        var defaultValue = source.GetDefaultValue();
        defaultValue.Should().NotBeNull();
        defaultValue!.GetValue<string>().Should().Be("test value");
    }

    [Fact]
    public void GivenR5Map_WhenCheckingSupportsConstants_ThenReturnsTrue()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R5;

        // Act
        var supportsConstants = map.SupportsConstants();

        // Assert
        supportsConstants.Should().BeTrue();
    }

    [Fact]
    public void GivenR4Map_WhenCheckingSupportsConstants_ThenReturnsFalse()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R4;

        // Act
        var supportsConstants = map.SupportsConstants();

        // Assert
        supportsConstants.Should().BeFalse();
    }

    [Fact]
    public void GivenR4Map_WhenUsingGetConstantsOrEmpty_ThenReturnsEmpty()
    {
        // Arrange
        var map = new StructureMapJsonNode();
        map.FhirVersion = FhirVersion.R4;

        // Act
        var constants = map.GetConstantsOrEmpty().ToList();

        // Assert
        constants.Should().BeEmpty();
    }

    #endregion

    #region Round-Trip Fidelity

    [Fact]
    public void GivenR4StructureMapWithTypedDefaultValue_WhenRoundTripping_ThenPreservesType()
    {
        // Arrange
        var source = new StructureMapSourceJsonNode(new JsonObject(), FhirVersion.R4);
        source.SetDefaultValue("Integer", JsonValue.Create(42));

        // Act - serialize and deserialize
        var json = source.MutableNode.ToJsonString();
        var parsed = JsonNode.Parse(json) as JsonObject;
        var roundTripped = new StructureMapSourceJsonNode(parsed!, FhirVersion.R4);

        // Assert
        var defaultValue = roundTripped.GetDefaultValue();
        defaultValue.Should().NotBeNull();
        defaultValue!.GetValue<int>().Should().Be(42);

        // Check the property name is preserved
        roundTripped.MutableNode.ContainsKey("defaultValueInteger").Should().BeTrue();
    }

    #endregion
}
