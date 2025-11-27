// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests to ensure all BaseJsonNode-derived types have the required constructor
/// for JsonNodeConverter to work correctly.
///
/// JsonNodeConverter.Read() uses Activator.CreateInstance with [JsonObject, null] parameters,
/// so all types must have a constructor with signature (JsonObject, FhirSpecification?).
/// </summary>
public class JsonNodeConverterConstructorTests
{
    /// <summary>
    /// All BaseJsonNode-derived types in Ignixa.Serialization.Models must have a constructor
    /// that accepts (JsonObject, FhirSpecification?) for JsonNodeConverter to work.
    /// </summary>
    [Fact]
    public void AllBaseJsonNodeTypes_ShouldHaveRequiredConstructor()
    {
        // Arrange - Get all types deriving from BaseJsonNode in the Models namespace
        var baseJsonNodeTypes = typeof(ResourceJsonNode).Assembly
            .GetTypes()
            .Where(t => typeof(BaseJsonNode).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && t.IsClass
                        && t.Namespace?.Contains("Models", StringComparison.Ordinal) == true)
            .ToList();

        var missingConstructors = new List<string>();

        // Act - Check each type for the required constructor
        foreach (var type in baseJsonNodeTypes)
        {
            var hasRequiredConstructor = CanCreateWithJsonNodeConverter(type);
            if (!hasRequiredConstructor)
            {
                missingConstructors.Add(type.Name);
            }
        }

        // Assert
        missingConstructors.Should().BeEmpty(
            $"The following types are missing the (JsonObject, FhirSpecification?) constructor required by JsonNodeConverter: {string.Join(", ", missingConstructors)}. " +
            "Add: public TypeName(JsonObject jsonObject, FhirSpecification? fhirVersion = null) : base(jsonObject, fhirVersion) {{ }}");
    }

    /// <summary>
    /// Verify specific known types can be directly deserialized (regression test).
    /// </summary>
    [Fact]
    public void BundleJsonNode_ShouldDeserializeDirectly()
    {
        var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>("""{"resourceType":"Bundle","type":"searchset"}""");
        bundle.Should().NotBeNull();
        bundle.Type.Should().Be(BundleJsonNode.BundleType.Searchset);
    }

    [Fact]
    public void OperationOutcomeJsonNode_ShouldDeserializeDirectly()
    {
        var outcome = JsonSourceNodeFactory.Parse<OperationOutcomeJsonNode>("""{"resourceType":"OperationOutcome"}""");
        outcome.Should().NotBeNull();
        outcome.ResourceType.Should().Be("OperationOutcome");
    }

    [Fact]
    public void ParametersJsonNode_ShouldDeserializeDirectly()
    {
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>("""{"resourceType":"Parameters"}""");
        parameters.Should().NotBeNull();
        parameters.ResourceType.Should().Be("Parameters");
    }

    private static bool CanCreateWithJsonNodeConverter(Type type)
    {
        // This mimics what JsonNodeConverter.Read does
        try
        {
            var testObject = new JsonObject();
            var instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [testObject, null],
                CultureInfo.InvariantCulture);
            return instance != null;
        }
        catch
        {
            return false;
        }
    }
}
