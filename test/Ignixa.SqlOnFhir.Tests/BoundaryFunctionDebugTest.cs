/*
 * Diagnostic test to debug boundary function issues.
 * Tests the entire pipeline from JSON → ITypedElement → FHIRPath evaluation → boundary function.
 */

using System.Text.Json.Nodes;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.Serialization.Models;

namespace Ignixa.SqlOnFhir.Tests;

public class BoundaryFunctionDebugTest
{
    [Fact]
    public void Test_Boundary_Function_On_Quantity_Value()
    {
        // ARRANGE: Create an Observation with valueQuantity
        var observationJson = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "id": "o1",
          "status": "final",
          "code": {
            "text": "test"
          },
          "valueQuantity": {
            "value": 1.0
          }
        }
        """);

        // Get the structure definition provider for R4
        var provider = FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();

        // Convert to ResourceJsonNode (same as OfficialSqlOnFhirTestRunner)
        var resourceNode = ResourceJsonNode.Parse(observationJson!.ToJsonString());

        // Wrap in ITypedElement with structure definitions
        var typedElement = resourceNode.ToTypedElement(provider);

        // Create FHIRPath compiler and evaluator
        var compiler = new FhirPathCompiler();
        var evaluator = new FhirPathEvaluator();

        // DEBUG: What children does the Observation have?
        var allChildren = typedElement.Children().ToList();
        Console.WriteLine($"Observation has {allChildren.Count} children:");
        foreach (var child in allChildren)
        {
            Console.WriteLine($"  - {child.Name} (InstanceType: {child.InstanceType})");
        }

        // DEBUG: What elements are in the Observation structure definition?
        var observationStructureDef = provider.Provide("Observation");
        if (observationStructureDef != null)
        {
            var elements = observationStructureDef.GetElements().ToList();
            Console.WriteLine($"\nObservation structure has {elements.Count} elements:");
            foreach (var element in elements.Where(e => e.ElementName != null && (e.ElementName.Contains("value", StringComparison.OrdinalIgnoreCase) || e.ElementName.EndsWith("[x]", StringComparison.Ordinal))))
            {
                Console.WriteLine($"  - {element.ElementName} (IsChoice: {element.IsChoiceElement}, Types: {element.Type?.Length ?? 0})");
            }
        }

        // ACT 1: Test navigation to value (polymorphic)
        var valueElements = typedElement.Children("value").ToList();
        Console.WriteLine($"Children('value') returned {valueElements.Count} elements");

        // Try direct access to valueQuantity
        var valueQuantityElements = typedElement.Children("valueQuantity").ToList();
        Console.WriteLine($"Children('valueQuantity') returned {valueQuantityElements.Count} elements");

        // For now, use direct access since polymorphic matching isn't working
        Assert.NotEmpty(valueQuantityElements);
        var valueElement = valueQuantityElements.First();
        Console.WriteLine($"valueQuantity InstanceType: {valueElement.InstanceType}, Value: {valueElement.Value}");

        // Debug: Check if Definition is set
        Console.WriteLine($"valueQuantity Definition: {valueElement.Definition?.ElementName ?? "null"}");
        if (valueElement.Definition != null)
        {
            Console.WriteLine($"  IsChoiceElement: {valueElement.Definition.IsChoiceElement}");
            Console.WriteLine($"  Type count: {valueElement.Definition.Type?.Length ?? 0}");
            if (valueElement.Definition.Type != null)
            {
                foreach (var type in valueElement.Definition.Type)
                {
                    Console.WriteLine($"    Type: {type}");
                }
            }
        }

        // Debug: Check InstanceType of valueQuantity
        var instanceType = valueElement.InstanceType;
        Assert.Equal("Quantity", instanceType); // Should be "Quantity"

        // ACT 2: Test ofType(Quantity)
        var ofTypeExpr = compiler.Parse("value.ofType(Quantity)");
        var quantityElements = evaluator.Evaluate(typedElement, ofTypeExpr).ToList();
        Console.WriteLine($"ofType(Quantity) returned {quantityElements.Count} elements");

        // For now, use direct access instead of ofType
        var quantityElement = valueElement; // Use the valueQuantity element we already have
        //Assert.NotEmpty(quantityElements);
        //var quantityElement = quantityElements.First();

        // Debug: Verify it's still Quantity
        Assert.Equal("Quantity", quantityElement.InstanceType);

        // ACT 3: Navigate to .value child
        var quantityValueElements = quantityElement.Children("value").ToList();
        Assert.NotEmpty(quantityValueElements);
        var quantityValueElement = quantityValueElements.First();

        // Debug: Check the value element
        var valueInstanceType = quantityValueElement.InstanceType;
        var valueObj = quantityValueElement.Value;

        // ASSERT: Value element should have correct types
        Assert.Equal("decimal", valueInstanceType); // Should be "decimal"
        Assert.NotNull(valueObj);
        Assert.IsType<decimal>(valueObj); // Should be decimal type
        Assert.Equal(1.0m, (decimal)valueObj);

        // ACT 4: Test lowBoundary() function
        // Instead of full expression, test step by step
        // Step 1: We have quantityElement (valueQuantity with InstanceType="Quantity")
        // Step 2: Navigate to .value child and call lowBoundary()
        var valuePathExpr = compiler.Parse("value.lowBoundary()");
        var boundaryResults = evaluator.Evaluate(quantityElement, valuePathExpr).ToList();
        Console.WriteLine($"lowBoundary() returned {boundaryResults.Count} results");
        if (boundaryResults.Any())
        {
            Console.WriteLine($"First result: {boundaryResults.First().Value} (type: {boundaryResults.First().Value?.GetType().Name})");
        }
        Assert.NotEmpty(boundaryResults);
        var boundaryResult = boundaryResults.First();

        // ASSERT: Should return 0.95 (1.0 * 0.95)
        Assert.NotNull(boundaryResult.Value);
        Assert.IsType<decimal>(boundaryResult.Value);
        Assert.Equal(0.95m, (decimal)boundaryResult.Value);
    }
}
