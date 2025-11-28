// <copyright file="AuditEventReferenceExtractionTest.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;
using static Ignixa.Serialization.SourceNodes.SchemaAwareElementExtensions;
using IElement = Ignixa.Abstractions.IElement;

namespace Ignixa.Serialization.Tests;

public class AuditEventReferenceExtractionTest
{
    private readonly ITestOutputHelper _output;
    private readonly R4CoreSchemaProvider _provider = new();

    public AuditEventReferenceExtractionTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenAuditEvent_WhenExtractingAgentWho_ThenInstanceTypeIsReference()
    {
        // Arrange
        var json = @"{
    ""resourceType"": ""AuditEvent"",
    ""type"": {
        ""system"": ""system"",
        ""code"": ""code""
    },
    ""recorded"": ""2021-05-28T00:00:00.000"",
    ""agent"": [
        {
            ""who"": {
                ""reference"": ""Practitioner/searchpractitioner3""
            }
        }
    ],
    ""source"": {
        ""observer"": {
            ""reference"": ""Patient/searchpatient2""
        }
    },
    ""entity"": [
        {
            ""what"": {
                ""reference"": ""Observation/searchobservation2""
            }
        }
    ]
}";

        // Act
        ISourceNavigator sourceNode = JsonSourceNodeFactory.Parse(json).ToSourceNavigator();
        IElement element = sourceNode.ToElement(_provider);

        _output.WriteLine("=== Root AuditEvent ===");
        _output.WriteLine($"InstanceType: {element.InstanceType}");
        _output.WriteLine($"Name: {element.Name}");

        // Test AuditEvent.agent.who expression
        var agentWhoExpression = "AuditEvent.agent.who";
        var agentWhoResults = element.Select(agentWhoExpression).ToArray();

        _output.WriteLine($"\n=== FHIRPath: {agentWhoExpression} ===");
        _output.WriteLine($"Results count: {agentWhoResults.Length}");

        foreach (var result in agentWhoResults)
        {
            _output.WriteLine($"\nAgent.who element:");
            _output.WriteLine($"  Name: {result.Name}");
            _output.WriteLine($"  InstanceType: {result.InstanceType ?? "NULL"}");
            _output.WriteLine($"  Value: {result.Value}");

            var referenceChildren = result.Children("reference").ToArray();
            if (referenceChildren.Length > 0)
            {
                var referenceChild = referenceChildren[0];
                _output.WriteLine($"  reference child:");
                _output.WriteLine($"    Name: {referenceChild.Name}");
                _output.WriteLine($"    InstanceType: {referenceChild.InstanceType ?? "NULL"}");
                _output.WriteLine($"    Value: {referenceChild.Value}");
            }

            // Get the Type to understand the type information
            if (result is IElement elementResult && elementResult.Type != null)
            {
                _output.WriteLine($"  Type:");
                _output.WriteLine($"    ElementName: {elementResult.Type.Info.Name}");
                _output.WriteLine($"    IsChoiceElement: {elementResult.Type.Info.IsChoiceElement}");
                _output.WriteLine($"    Type count: {elementResult.Type.Children.Count}");
                foreach (var type in elementResult.Type.Children)
                {
                    var referredType = type?.GetType().GetProperty("ReferredType")?.GetValue(type)?.ToString();
                    if (referredType != null)
                    {
                        _output.WriteLine($"      - ReferredType: {referredType}");
                    }
                    else
                    {
                        _output.WriteLine($"      - Type: {type?.GetType().Name}");
                    }
                }
            }
            else
            {
                _output.WriteLine($"  Type: NULL");
            }
        }

        // Test AuditEvent.entity.what expression
        var entityWhatExpression = "AuditEvent.entity.what";
        var entityWhatResults = element.Select(entityWhatExpression).ToArray();

        _output.WriteLine($"\n=== FHIRPath: {entityWhatExpression} ===");
        _output.WriteLine($"Results count: {entityWhatResults.Length}");

        foreach (var result in entityWhatResults)
        {
            _output.WriteLine($"\nEntity.what element:");
            _output.WriteLine($"  Name: {result.Name}");
            _output.WriteLine($"  InstanceType: {result.InstanceType ?? "NULL"}");
            _output.WriteLine($"  Value: {result.Value}");
        }

        // Assert
        Assert.Single(agentWhoResults);
        Assert.NotNull(agentWhoResults[0].InstanceType);
        Assert.Equal("Reference", agentWhoResults[0].InstanceType);

        Assert.Single(entityWhatResults);
        Assert.NotNull(entityWhatResults[0].InstanceType);
        Assert.Equal("Reference", entityWhatResults[0].InstanceType);
    }

    [Fact]
    public void GivenAuditEvent_WhenInspectingStructureDefinition_ThenAgentWhoHasReferenceType()
    {
        // Arrange
        var auditEventType = _provider.GetTypeDefinition("AuditEvent");
        Assert.NotNull(auditEventType);

        // Act
        var elements = auditEventType.Children;
        var agentElement = elements.FirstOrDefault(e => e.Info.Name == "agent");

        _output.WriteLine("=== AuditEvent.agent ===");
        if (agentElement != null)
        {
            _output.WriteLine($"ElementName: {agentElement.Info.Name}");
            _output.WriteLine($"IsCollection: {agentElement.IsCollection}");

            // Get child elements (BackboneElement)
            var backboneChildren = agentElement.Children;
            _output.WriteLine($"Child count: {backboneChildren.Count}");

            var whoElement = backboneChildren.FirstOrDefault(e => e.Info.Name == "who");

            _output.WriteLine($"\n=== AuditEvent.agent.who (from BackboneElement) ===");
            if (whoElement != null)
            {
                _output.WriteLine($"  ElementName: {whoElement.Info.Name}");

                // Check if this has type information (cast to ITypeExtended for Types property)
                if (whoElement is ITypeExtended whoExtended)
                {
                    _output.WriteLine($"  Type count: {whoExtended.Types.Count}");
                    foreach (var whoType in whoExtended.Types)
                    {
                        _output.WriteLine($"    - Code: {whoType.Code}");
                    }
                }
            }
            else
            {
                _output.WriteLine($"  who element NOT FOUND");
            }
        }
        else
        {
            _output.WriteLine("agent element NOT FOUND");
        }
    }
}
