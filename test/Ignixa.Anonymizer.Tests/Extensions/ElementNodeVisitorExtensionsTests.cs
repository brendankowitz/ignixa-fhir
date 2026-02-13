// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Visitors;

namespace Ignixa.Anonymizer.Tests.Extensions;

public class ElementNodeVisitorExtensionsTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public void GivenAPatientNode_WhenVisit_ThenAllNodesShouldBeVisited()
    {
        // Arrange
        var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test"},{}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var result = new HashSet<string>();

        // Act
        element.Accept(resourceNode, new TestVisitor(result));

        // Assert
        result.ShouldContain("Patient");
        result.ShouldContain("Patient.active");
        result.ShouldContain("Patient.address[0]");
        result.ShouldContain("Patient.address[0].city");
        result.ShouldContain("Patient.address[1]");
    }

    [Fact]
    public void GivenAPatientNodeWithContained_WhenVisit_ThenAllNodesIncludingContainedShouldBeVisited()
    {
        // Arrange
        var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test"},{}],"contained":[{"resourceType":"Observation","status":"unknown"}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var result = new HashSet<string>();

        // Act
        element.Accept(resourceNode, new TestVisitor(result));

        // Assert
        result.ShouldContain("Patient");
        result.ShouldContain("Patient.active");
        result.ShouldContain("Patient.address[0]");
        result.ShouldContain("Patient.address[0].city");
        result.ShouldContain("Patient.address[1]");
        result.ShouldContain("Patient.contained[0]");
        result.ShouldContain("Patient.contained[0].status");
    }

    [Fact]
    public void GivenABundleNode_WhenVisit_ThenAllNodesIncludingEntryResourceShouldBeVisited()
    {
        // Arrange
        var json = """{"resourceType":"Bundle","type":"document","entry":[{"fullUrl":"http://example.org/fhir/Patient/1","resource":{"resourceType":"Patient","active":true}}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var result = new HashSet<string>();

        // Act
        element.Accept(resourceNode, new TestVisitor(result));

        // Assert
        result.ShouldContain("Bundle");
        result.ShouldContain("Bundle.type");
        result.ShouldContain("Bundle.entry[0]");
        result.ShouldContain("Bundle.entry[0].fullUrl");
        result.ShouldContain("Bundle.entry[0].resource");
        result.ShouldContain("Bundle.entry[0].resource.active");
    }

    private class TestVisitor(HashSet<string> result) : AbstractElementNodeVisitor
    {
        public override bool Visit(ResourceJsonNode resource, IElement node)
        {
            result.Add(node.Location);
            return true;
        }
    }
}
