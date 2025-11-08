// <copyright file="R4StructureDefinitionProviderComparisonTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.ElementModel; // SDK ElementModel (ISourceNode, ITypedElement, ToTypedElement extensions)
using Hl7.FhirPath; // SDK FhirPath extensions
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Abstractions;
// Our FhirPath extensions
using Ignixa.Serialization.Tests.TestData;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;

// Namespace aliases to avoid conflicts

// Static using for our extension methods
using static Ignixa.Serialization.SourceNodes.TypedElementExtensions;
using ISourceNode = Ignixa.Abstractions.ISourceNode;
using ITypedElement = Ignixa.Abstractions.ITypedElement;

// SDK type aliases
using SdkModelInspector = Hl7.Fhir.Introspection.ModelInspector;
using SdkIStructureDefinitionSummaryProvider = Hl7.Fhir.Specification.IStructureDefinitionSummaryProvider;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests to compare behavior between Firely SDK's ModelInfo.ModelInspector
/// and our generated R4StructureDefinitionSummaryProvider.
/// </summary>
public class R4StructureDefinitionProviderComparisonTests
{
    private readonly ITestOutputHelper _output;
    private readonly R4StructureDefinitionSummaryProvider _ourProvider = new();
    private readonly SdkModelInspector _firelyProvider = ModelInfo.ModelInspector;

    public R4StructureDefinitionProviderComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenPatientResource_WhenGettingStructureDefinition_ThenBothProvidersReturnSummary()
    {
        // Arrange
        string typeName = "Patient";

        // Act
        var ourSummary = _ourProvider.Provide(typeName);
        var firelySummary = _firelyProvider.Provide(typeName);

        // Assert
        Assert.NotNull(ourSummary);
        Assert.NotNull(firelySummary);

        _output.WriteLine($"Our Provider - TypeName: {ourSummary.TypeName}, IsResource: {ourSummary.IsResource}");
        _output.WriteLine($"Firely Provider - TypeName: {firelySummary.TypeName}, IsResource: {firelySummary.IsResource}");

        Assert.Equal(firelySummary.TypeName, ourSummary.TypeName);
        Assert.Equal(firelySummary.IsResource, ourSummary.IsResource);
    }

    [Fact]
    public void GivenStringPrimitiveType_WhenProviding_ThenBothProvidersReturnSummary()
    {
        // Arrange - try both the simple name and the FHIRPath URL
        string simpleTypeName = "string";
        string fhirPathUrl = "http://hl7.org/fhirpath/System.String";

        // Act
        var ourSimple = _ourProvider.Provide(simpleTypeName);
        var firelySimple = _firelyProvider.Provide(simpleTypeName);
        var ourFhirPath = _ourProvider.Provide(fhirPathUrl);
        var firelyFhirPath = _firelyProvider.Provide(fhirPathUrl);

        // Assert
        _output.WriteLine($"Simple 'string' type:");
        _output.WriteLine($"  Our Provider: {(ourSimple != null ? ourSimple.TypeName : "NULL")}");
        _output.WriteLine($"  Firely Provider: {(firelySimple != null ? firelySimple.TypeName : "NULL")}");

        _output.WriteLine($"FHIRPath URL 'http://hl7.org/fhirpath/System.String':");
        _output.WriteLine($"  Our Provider: {(ourFhirPath != null ? ourFhirPath.TypeName : "NULL")}");
        _output.WriteLine($"  Firely Provider: {(firelyFhirPath != null ? firelyFhirPath.TypeName : "NULL")}");

        Assert.NotNull(firelySimple);
        Assert.NotNull(ourSimple);

        // This is the key test - can both providers handle the FHIRPath URL?
        if (firelyFhirPath != null)
        {
            Assert.NotNull(ourFhirPath); // Our provider should also handle it
        }
    }

    [Fact]
    public void GivenPatientIdElement_WhenGettingType_ThenCompareProviders()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");

        // Our implementation
        ISourceNode ourSourceNode = JsonSourceNodeFactory.Parse(patientJson).ToSourceNode();
        ITypedElement ourTypedElement = ourSourceNode.ToTypedElement(_ourProvider);
        ITypedElement ourId = ourTypedElement.Select("Patient.id").SingleOrDefault();

        // SDK implementation
        SdkISourceNode firelySourceNode = Hl7.Fhir.Serialization.FhirJsonNode.Parse(patientJson);
        SdkITypedElement firelyTypedElement = firelySourceNode.ToTypedElement(_firelyProvider);
        SdkITypedElement firelyId = firelyTypedElement.Select("Patient.id").SingleOrDefault();

        // Assert
        Assert.NotNull(ourId);
        Assert.NotNull(firelyId);

        _output.WriteLine($"Our Provider - Id element type: {ourId.InstanceType}");
        _output.WriteLine($"Firely Provider - Id element type: {firelyId.InstanceType}");

        _output.WriteLine($"Our Provider - Id value: {ourId.Value}");
        _output.WriteLine($"Firely Provider - Id value: {firelyId.Value}");

        // Values should match
        Assert.Equal(firelyId.Value, ourId.Value);
    }

    [Fact]
    public void GivenPatientSummary_WhenGettingElements_ThenCompareElementCounts()
    {
        // Arrange
        var ourSummary = _ourProvider.Provide("Patient");
        var firelySummary = _firelyProvider.Provide("Patient");

        Assert.NotNull(ourSummary);
        Assert.NotNull(firelySummary);

        // Act
        var ourElements = ourSummary.GetElements();
        var firelyElements = firelySummary.GetElements();

        // Assert
        _output.WriteLine($"Our Provider - Element count: {ourElements.Count}");
        _output.WriteLine($"Firely Provider - Element count: {firelyElements.Count}");

        // Log first few elements from each
        _output.WriteLine("\nOur Provider - First 5 elements:");
        foreach (var element in ourElements.Take(5))
        {
            var typeInfo = element.Type.FirstOrDefault();
            string typeName = typeInfo is IStructureDefinitionSummary summary ? summary.TypeName : "NO TYPE";
            _output.WriteLine($"  {element.ElementName}: {typeName}");
        }

        _output.WriteLine("\nFirely Provider - First 5 elements:");
        foreach (var element in firelyElements.Take(5))
        {
            var typeInfo = element.Type.FirstOrDefault();
            string typeName = typeInfo is IStructureDefinitionSummary summary ? summary.TypeName : "NO TYPE";
            _output.WriteLine($"  {element.ElementName}: {typeName}");
        }
    }
}
