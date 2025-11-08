// <copyright file="AuditEventReferenceExtractionTest.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;
using static Ignixa.Serialization.SourceNodes.TypedElementExtensions;
using ISourceNode = Ignixa.Abstractions.ISourceNode;
using ITypedElement = Ignixa.Abstractions.ITypedElement;

namespace Ignixa.Serialization.Tests;

public class AuditEventReferenceExtractionTest
{
    private readonly ITestOutputHelper _output;
    private readonly R4StructureDefinitionSummaryProvider _provider = new();

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
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(json).ToSourceNode();
        ITypedElement typedElement = sourceNode.ToTypedElement(_provider);

        _output.WriteLine("=== Root AuditEvent ===");
        _output.WriteLine($"InstanceType: {typedElement.InstanceType}");
        _output.WriteLine($"Name: {typedElement.Name}");

        // Test AuditEvent.agent.who expression
        var agentWhoExpression = "AuditEvent.agent.who";
        var agentWhoResults = typedElement.Select(agentWhoExpression).ToArray();

        _output.WriteLine($"\n=== FHIRPath: {agentWhoExpression} ===");
        _output.WriteLine($"Results count: {agentWhoResults.Length}");

        foreach (var result in agentWhoResults)
        {
            _output.WriteLine($"\nAgent.who element:");
            _output.WriteLine($"  Name: {result.Name}");
            _output.WriteLine($"  InstanceType: {result.InstanceType ?? "NULL"}");
            _output.WriteLine($"  Value: {result.Value}");

            var referenceChild = result.Children("reference").FirstOrDefault();
            if (referenceChild != null)
            {
                _output.WriteLine($"  reference child:");
                _output.WriteLine($"    Name: {referenceChild.Name}");
                _output.WriteLine($"    InstanceType: {referenceChild.InstanceType ?? "NULL"}");
                _output.WriteLine($"    Value: {referenceChild.Value}");
            }

            // Get the Definition to understand the type information
            if (result.Definition != null)
            {
                _output.WriteLine($"  Definition:");
                _output.WriteLine($"    ElementName: {result.Definition.ElementName}");
                _output.WriteLine($"    IsChoiceElement: {result.Definition.IsChoiceElement}");
                _output.WriteLine($"    Type count: {result.Definition.Type.Length}");
                foreach (var type in result.Definition.Type)
                {
                    if (type is IStructureDefinitionSummary summary)
                    {
                        _output.WriteLine($"      - TypeName: {summary.TypeName}");
                    }
                    else if (type is IStructureDefinitionReference reference)
                    {
                        _output.WriteLine($"      - ReferredType: {reference.ReferredType}");
                    }
                    else
                    {
                        _output.WriteLine($"      - Type: {type.GetType().Name}");
                    }
                }
            }
            else
            {
                _output.WriteLine($"  Definition: NULL");
            }
        }

        // Test AuditEvent.entity.what expression
        var entityWhatExpression = "AuditEvent.entity.what";
        var entityWhatResults = typedElement.Select(entityWhatExpression).ToArray();

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
        var auditEventSummary = _provider.Provide("AuditEvent");
        Assert.NotNull(auditEventSummary);

        // Act
        var elements = auditEventSummary.GetElements();
        var agentElement = elements.FirstOrDefault(e => e.ElementName == "agent");

        _output.WriteLine("=== AuditEvent.agent ===");
        if (agentElement != null)
        {
            _output.WriteLine($"ElementName: {agentElement.ElementName}");
            _output.WriteLine($"IsCollection: {agentElement.IsCollection}");
            _output.WriteLine($"Type count: {agentElement.Type.Length}");

            // Check if we can get the BackboneElement type
            if (agentElement.Type.Length > 0)
            {
                foreach (var type in agentElement.Type)
                {
                    if (type is IStructureDefinitionSummary summary)
                    {
                        _output.WriteLine($"  Type: {summary.TypeName}");
                        _output.WriteLine($"  IsAbstract: {summary.IsAbstract}");

                        // Get the elements of the BackboneElement
                        var backboneElements = summary.GetElements();
                        var whoElement = backboneElements.FirstOrDefault(e => e.ElementName == "who");

                        _output.WriteLine($"\n=== AuditEvent.agent.who (from BackboneElement) ===");
                        if (whoElement != null)
                        {
                            _output.WriteLine($"  ElementName: {whoElement.ElementName}");
                            _output.WriteLine($"  Type count: {whoElement.Type.Length}");
                            foreach (var whoType in whoElement.Type)
                            {
                                if (whoType is IStructureDefinitionSummary whoSummary)
                                {
                                    _output.WriteLine($"    - TypeName: {whoSummary.TypeName}");
                                }
                                else if (whoType is IStructureDefinitionReference whoReference)
                                {
                                    _output.WriteLine($"    - ReferredType: {whoReference.ReferredType}");
                                }
                                else
                                {
                                    _output.WriteLine($"    - Type: {whoType.GetType().Name}");
                                }
                            }
                        }
                        else
                        {
                            _output.WriteLine($"  who element NOT FOUND");
                        }
                    }
                }
            }
        }
        else
        {
            _output.WriteLine("agent element NOT FOUND");
        }
    }
}
