// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class ResourceTreeWalkerTests
{
    [Fact]
    public void GivenResourceWithInfrastructureKeys_WhenWalked_ThenInfrastructurePathsNotYielded()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "123",
              "meta": {
                "lastUpdated": "2025-01-01T00:00:00Z"
              },
              "text": {
                "div": "<div>Patient</div>"
              },
              "implicitRules": "http://example.com/rules",
              "birthDate": "1990-03-15",
              "name": [
                {
                  "family": "Smith"
                }
              ]
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        targets.ShouldNotContain(t => t.Path.StartsWith("id"));
        targets.ShouldNotContain(t => t.Path.StartsWith("meta"));
        targets.ShouldNotContain(t => t.Path.StartsWith("text"));
        targets.ShouldNotContain(t => t.Path.StartsWith("implicitRules"));
        targets.ShouldNotContain(t => t.Path.StartsWith("resourceType"));

        targets.ShouldContain(t => t.Path == "birthDate");
        targets.ShouldContain(t => t.Path == "name[0].family");
    }

    [Fact]
    public void GivenResourceWithNonStringLeaves_WhenWalked_ThenOnlyStringLeavesYielded()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "456",
              "multipleBirthInteger": 2,
              "active": true,
              "deceasedBoolean": null,
              "name": [
                {
                  "family": "Smith"
                }
              ]
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        targets.ShouldNotContain(t => t.ElementName == "multipleBirthInteger");
        targets.ShouldNotContain(t => t.ElementName == "active");
        targets.ShouldNotContain(t => t.ElementName == "deceasedBoolean");

        targets.ShouldContain(t => t.ElementName == "family");
    }

    [Fact]
    public void GivenNestedObjectInArray_WhenWalked_ThenPathsAreDottedAndIndexed()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "789",
              "name": [
                {
                  "family": "Doe",
                  "given": [
                    "Alice",
                    "Jane"
                  ]
                }
              ]
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        targets.ShouldContain(t => t.Path == "name[0].family");
        targets.ShouldContain(t => t.Path == "name[0].given[0]");
        targets.ShouldContain(t => t.Path == "name[0].given[1]");
    }

    [Fact]
    public void GivenScalarArrayWithTwoElements_WhenWalked_ThenBothIndexedPathsYielded()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "999",
              "name": [
                {
                  "given": [
                    "Alice",
                    "Jane"
                  ]
                }
              ]
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        var aliceTarget = targets.FirstOrDefault(t => t.Path == "name[0].given[0]");
        aliceTarget.ShouldNotBeNull();
        aliceTarget.Value.ShouldBe("Alice");
        aliceTarget.ShouldBeOfType<ArrayItemTarget>();

        var janeTarget = targets.FirstOrDefault(t => t.Path == "name[0].given[1]");
        janeTarget.ShouldNotBeNull();
        janeTarget.Value.ShouldBe("Jane");
        janeTarget.ShouldBeOfType<ArrayItemTarget>();
    }

    [Fact]
    public void GivenPropertyLeaf_WhenReplaced_ThenUnderlyingJsonMutated()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "111",
              "birthDate": "1990-03-15"
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        var birthDateTarget = targets.FirstOrDefault(t => t.Path == "birthDate");
        birthDateTarget.ShouldNotBeNull();
        birthDateTarget.ShouldBeOfType<PropertyTarget>();

        birthDateTarget.Replace("2000-01-01");

        var mutatedValue = resource.MutableNode["birthDate"]!.GetValue<string>();
        mutatedValue.ShouldBe("2000-01-01");

        var jsonString = resource.MutableNode.ToJsonString();
        jsonString.ShouldContain("2000-01-01");
    }

    [Fact]
    public void GivenArrayItemLeaf_WhenReplaced_ThenUnderlyingJsonMutated()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "222",
              "name": [
                {
                  "given": [
                    "Original"
                  ]
                }
              ]
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        var givenTarget = targets.FirstOrDefault(t => t.Path == "name[0].given[0]");
        givenTarget.ShouldNotBeNull();
        givenTarget.ShouldBeOfType<ArrayItemTarget>();

        givenTarget.Replace("Mutated");

        var jsonString = resource.MutableNode.ToJsonString();
        jsonString.ShouldContain("Mutated");
        jsonString.ShouldNotContain("Original");
    }

    [Fact]
    public void GivenResourceWithOnlyInfrastructureKeys_WhenWalked_ThenNoTargetsYielded()
    {
        var json = """
            {
              "resourceType": "Patient",
              "id": "333",
              "meta": {
                "lastUpdated": "2025-01-01T00:00:00Z"
              },
              "text": {
                "div": "<div>Patient</div>"
              }
            }
            """;

        var resource = ResourceJsonNode.Parse(json);
        var targets = ResourceTreeWalker.Walk(resource);

        targets.Count.ShouldBe(0);
    }
}
