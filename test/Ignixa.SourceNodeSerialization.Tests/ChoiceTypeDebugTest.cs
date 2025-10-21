// <copyright file="ChoiceTypeDebugTest.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.ElementModel; // SDK ElementModel (ISourceNode, ITypedElement, ToTypedElement extensions)
using Hl7.FhirPath; // SDK FhirPath extensions
using Ignixa.FhirPath.Evaluation;
using Ignixa.SourceNodeSerialization.Abstractions; // Our FhirPath extensions
using Ignixa.SourceNodeSerialization.Specification;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;

// Namespace aliases to avoid conflicts

// Static using for our extension methods
using static Ignixa.SourceNodeSerialization.SourceNodes.TypedElementExtensions;
using ISourceNode = Ignixa.SourceNodeSerialization.Abstractions.ISourceNode;
using ITypedElement = Ignixa.SourceNodeSerialization.Abstractions.ITypedElement;

// SDK type aliases
using SdkModelInspector = Hl7.Fhir.Introspection.ModelInspector;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;

namespace Ignixa.SourceNodeSerialization.Tests;

public class ChoiceTypeDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly R4StructureDefinitionSummaryProvider _ourProvider = new();
    private readonly SdkModelInspector _firelyProvider = ModelInfo.ModelInspector;

    public ChoiceTypeDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenObservation_WhenGettingEffectiveElement_ThenCompareProviders()
    {
        // Arrange
        var ourSummary = _ourProvider.Provide("Observation");
        var firelySummary = _firelyProvider.Provide("Observation");

        Assert.NotNull(ourSummary);
        Assert.NotNull(firelySummary);

        // Act
        var ourElements = ourSummary.GetElements();
        var firelyElements = firelySummary.GetElements();

        // Find effective[x] element in both providers
        var ourEffective = ourElements.FirstOrDefault(e => e.ElementName.StartsWith("effective", StringComparison.Ordinal));
        var firelyEffective = firelyElements.FirstOrDefault(e => e.ElementName.StartsWith("effective", StringComparison.Ordinal));

        // Assert
        _output.WriteLine($"Our Provider - Effective element:");
        if (ourEffective != null)
        {
            _output.WriteLine($"  ElementName: {ourEffective.ElementName}");
            _output.WriteLine($"  IsChoiceElement: {ourEffective.IsChoiceElement}");
            _output.WriteLine($"  Types: {string.Join(", ", ourEffective.Type.Select(t => t is IStructureDefinitionSummary s ? s.TypeName : "?"))}");
            _output.WriteLine($"  DefaultTypeName: {(ourEffective.Type.FirstOrDefault() is IStructureDefinitionSummary s ? s.TypeName : "N/A")}");
            _output.WriteLine($"  Type count: {ourEffective.Type.Length}");
        }
        else
        {
            _output.WriteLine($"  NOT FOUND");
        }

        _output.WriteLine($"\nFirely Provider - Effective element:");
        if (firelyEffective != null)
        {
            _output.WriteLine($"  ElementName: {firelyEffective.ElementName}");
            _output.WriteLine($"  IsChoiceElement: {firelyEffective.IsChoiceElement}");
            _output.WriteLine($"  Types: {string.Join(", ", firelyEffective.Type.Select(t => t is IStructureDefinitionSummary s ? s.TypeName : "?"))}");
            _output.WriteLine($"  DefaultTypeName: {(firelyEffective.Type.FirstOrDefault() is IStructureDefinitionSummary s ? s.TypeName : "N/A")}");
            _output.WriteLine($"  Type count: {firelyEffective.Type.Length}");
        }
        else
        {
            _output.WriteLine($"  NOT FOUND");
        }

        Assert.NotNull(ourEffective);
        Assert.NotNull(firelyEffective);
        Assert.Equal(firelyEffective.ElementName, ourEffective.ElementName);
        Assert.Equal(firelyEffective.IsChoiceElement, ourEffective.IsChoiceElement);

        // Check the actual type names
        _output.WriteLine($"\nOur type names:");
        foreach (var type in ourEffective.Type)
        {
            if (type is IStructureDefinitionSummary summary)
            {
                _output.WriteLine($"  - {summary.TypeName}");
            }
        }

        _output.WriteLine($"\nFirely type names:");
        foreach (var type in firelyEffective.Type)
        {
            if (type is IStructureDefinitionSummary summary)
            {
                _output.WriteLine($"  - {summary.TypeName}");
            }
        }
    }

    [Fact]
    public void GivenExtension_WhenGettingValueElement_ThenCompareProviders()
    {
        // Arrange
        var ourSummary = _ourProvider.Provide("Extension");
        var firelySummary = _firelyProvider.Provide("Extension");

        Assert.NotNull(ourSummary);
        Assert.NotNull(firelySummary);

        // Act
        var ourElements = ourSummary.GetElements();
        var firelyElements = firelySummary.GetElements();

        // Find value[x] element in both providers
        var ourValue = ourElements.FirstOrDefault(e => e.ElementName.StartsWith("value", StringComparison.Ordinal));
        var firelyValue = firelyElements.FirstOrDefault(e => e.ElementName.StartsWith("value", StringComparison.Ordinal));

        // Assert
        _output.WriteLine($"Our Provider - Value element:");
        if (ourValue != null)
        {
            _output.WriteLine($"  ElementName: {ourValue.ElementName}");
            _output.WriteLine($"  IsChoiceElement: {ourValue.IsChoiceElement}");
            _output.WriteLine($"  Type count: {ourValue.Type.Length}");
        }
        else
        {
            _output.WriteLine($"  NOT FOUND");
        }

        _output.WriteLine($"\nFirely Provider - Value element:");
        if (firelyValue != null)
        {
            _output.WriteLine($"  ElementName: {firelyValue.ElementName}");
            _output.WriteLine($"  IsChoiceElement: {firelyValue.IsChoiceElement}");
            _output.WriteLine($"  Type count: {firelyValue.Type.Length}");
        }
        else
        {
            _output.WriteLine($"  NOT FOUND");
        }

        Assert.NotNull(ourValue);
        Assert.NotNull(firelyValue);
        Assert.Equal(firelyValue.ElementName, ourValue.ElementName);
        Assert.Equal(firelyValue.IsChoiceElement, ourValue.IsChoiceElement);
    }

    [Fact]
    public void GivenExtensionWithValueCode_WhenParsingWithBothProviders_ThenCompareBehavior()
    {
        // This mimics the failing ReadExtension test
        var json = @"{
  ""resourceType"" : ""Patient"",
  ""meta"" : {
    ""extension"" : [
      {
        ""url"" : ""http://example.com/deleted-state"",
        ""valueCode"" : ""soft-deleted""
      }
    ]
  }
}";

        // Try with Firely provider first (should work)
        _output.WriteLine("=== Firely Provider ===");
        try
        {
            // Use SDK's ISourceNode for SDK testing
            SdkISourceNode firelySourceNode = Hl7.Fhir.Serialization.FhirJsonNode.Parse(json);
            SdkITypedElement firelyTyped = firelySourceNode.ToTypedElement(_firelyProvider);
            var firelyPath = "Resource.meta.extension.where(url = 'http://example.com/deleted-state')";
            var firelyResult = firelyTyped.Select(firelyPath).ToArray();
            _output.WriteLine($"Firely: Found {firelyResult.Length} extension(s)");

            if (firelyResult.Length > 0)
            {
                var ext = firelyResult[0];
                _output.WriteLine($"  Extension InstanceType: {ext.InstanceType}");
                var children = ext.Children().ToArray();
                _output.WriteLine($"  Children count: {children.Length}");
                foreach (var child in children)
                {
                    _output.WriteLine($"    - {child.Name}: {child.InstanceType} = {child.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Firely FAILED: {ex.Message}");
        }

        // Now try with our provider
        _output.WriteLine("\n=== Our Provider ===");
        try
        {
            // Use our ISourceNode for our testing
            ISourceNode ourSourceNode = JsonSourceNodeFactory.Parse(json).ToSourceNode();
            ITypedElement ourTyped = ourSourceNode.ToTypedElement(_ourProvider);
            var ourPath = "Resource.meta.extension.where(url = 'http://example.com/deleted-state')";
            var ourResult = ourTyped.Select(ourPath).ToArray();
            _output.WriteLine($"Ours: Found {ourResult.Length} extension(s)");

            if (ourResult.Length > 0)
            {
                var ext = ourResult[0];
                _output.WriteLine($"  Extension InstanceType: {ext.InstanceType}");
                var children = ext.Children().ToArray();
                _output.WriteLine($"  Children count: {children.Length}");
                foreach (var child in children)
                {
                    _output.WriteLine($"    - {child.Name}: {child.InstanceType} = {child.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Ours FAILED: {ex.Message}");
            _output.WriteLine($"  Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                _output.WriteLine($"  Inner: {ex.InnerException.Message}");
            }
        }
    }

    [Fact]
    public void GivenExtension_WhenInspectingValueElement_ThenShowDetailedMetadata()
    {
        // Get Extension summary from both providers
        var ourExtension = _ourProvider.Provide("Extension");
        var firelyExtension = _firelyProvider.Provide("Extension");

        Assert.NotNull(ourExtension);
        Assert.NotNull(firelyExtension);

        var ourElements = ourExtension.GetElements();
        var firelyElements = firelyExtension.GetElements();

        var ourValue = ourElements.FirstOrDefault(e => e.ElementName == "value" || e.ElementName.StartsWith("value", StringComparison.Ordinal));
        var firelyValue = firelyElements.FirstOrDefault(e => e.ElementName == "value" || e.ElementName.StartsWith("value", StringComparison.Ordinal));

        _output.WriteLine("=== OUR PROVIDER - Extension.value ===");
        if (ourValue != null)
        {
            _output.WriteLine($"ElementName: {ourValue.ElementName}");
            _output.WriteLine($"IsChoiceElement: {ourValue.IsChoiceElement}");
            _output.WriteLine($"IsCollection: {ourValue.IsCollection}");
            _output.WriteLine($"IsRequired: {ourValue.IsRequired}");
            _output.WriteLine($"Type count: {ourValue.Type.Length}");
            _output.WriteLine($"Types:");
            foreach (var type in ourValue.Type)
            {
                if (type is IStructureDefinitionSummary summary)
                {
                    _output.WriteLine($"  - {summary.TypeName}");
                }
            }

            // Check if we can lookup 'code' type
            _output.WriteLine($"\nCan our provider lookup 'code'? {_ourProvider.Provide("code") != null}");
            _output.WriteLine($"Can our provider lookup 'Code'? {_ourProvider.Provide("Code") != null}");

            // Check TypeName of code type in both providers
            var ourCodeType = _ourProvider.Provide("code");
            var firelyCodeType = _firelyProvider.Provide("code");
            _output.WriteLine($"\nOur 'code' TypeName: {ourCodeType?.TypeName}");
            _output.WriteLine($"Firely 'code' TypeName: {firelyCodeType?.TypeName}");

            // Check if the Type array elements match the Provide() lookups
            if (ourValue != null)
            {
                var codeTypeFromArray = ourValue.Type.FirstOrDefault(t => t is IStructureDefinitionSummary s && s.TypeName == "code");
                _output.WriteLine($"\nOur value.Type contains 'code'? {codeTypeFromArray != null}");
                if (codeTypeFromArray is IStructureDefinitionSummary codeSum)
                {
                    _output.WriteLine($"  Same instance as Provide('code')? {ReferenceEquals(codeSum, ourCodeType)}");
                }
            }
        }
        else
        {
            _output.WriteLine("NOT FOUND");
        }

        _output.WriteLine("\n=== FIRELY PROVIDER - Extension.value ===");
        if (firelyValue != null)
        {
            _output.WriteLine($"ElementName: {firelyValue.ElementName}");
            _output.WriteLine($"IsChoiceElement: {firelyValue.IsChoiceElement}");
            _output.WriteLine($"IsCollection: {firelyValue.IsCollection}");
            _output.WriteLine($"IsRequired: {firelyValue.IsRequired}");
            _output.WriteLine($"Type count: {firelyValue.Type.Length}");
            _output.WriteLine($"Types:");
            foreach (var type in firelyValue.Type)
            {
                if (type is IStructureDefinitionSummary summary)
                {
                    _output.WriteLine($"  - {summary.TypeName}");
                }
            }

            // Check if Firely can lookup 'code' type
            _output.WriteLine($"\nCan Firely lookup 'code'? {_firelyProvider.Provide("code") != null}");
            _output.WriteLine($"Can Firely lookup 'Code'? {_firelyProvider.Provide("Code") != null}");

            // Check TypeName of Code type in both providers
            var ourCodeTypeCap = _ourProvider.Provide("Code");
            var firelyCodeTypeCap = _firelyProvider.Provide("Code");
            _output.WriteLine($"\nOur 'Code' TypeName: {ourCodeTypeCap?.TypeName}");
            _output.WriteLine($"Firely 'Code' TypeName: {firelyCodeTypeCap?.TypeName}");
        }
        else
        {
            _output.WriteLine("NOT FOUND");
        }
    }
}
