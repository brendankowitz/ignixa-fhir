// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Shouldly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Definition;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Serialization.SourceNodes;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Models;
using Xunit.Abstractions;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// DIAGNOSTIC TEST: Debug composite search indexing pipeline to understand
/// why code-value-quantity isn't being indexed correctly.
///
/// This test creates an Observation with APGAR score and uses ElementSearchIndexer
/// to extract search indices, specifically checking the code-value-quantity composite.
/// </summary>
public class CompositeSearchIndexingDiagnosticTests
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISearchIndexer _indexer;
    private readonly ISearchParameterDefinitionManager _searchParamManager;

    public CompositeSearchIndexingDiagnosticTests()
    {
        _schemaProvider = new R4CoreSchemaProvider();
        _loggerFactory = NullLoggerFactory.Instance;

        // Create search parameter manager
        _searchParamManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // Create indexer using factory
        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            _loggerFactory,
            _searchParamManager);
    }

    /// <summary>
    /// Diagnostic test to understand the composite search indexing pipeline
    /// for code-value-quantity search parameter.
    ///
    /// Creates an Observation with:
    /// - code: "9272-6" (LOINC, APGAR 1-minute)
    /// - valueQuantity: 10 {score}
    ///
    /// Expected behavior:
    /// 1. FHIRPath expression for code-value-quantity should extract the root Observation
    /// 2. Component 0 (code) should extract TokenSearchValue from code.coding
    /// 3. Component 1 (value) should extract QuantitySearchValue from valueQuantity
    /// 4. Both components should be combined into CompositeSearchValue
    /// </summary>
    [Fact]
    public void GivenObservationWithCodeAndValueQuantity_WhenIndexing_ThenCompositeSearchValueExtracted()
    {
        // Arrange: Create APGAR Observation (same as E2E test fixture [0])
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        // Convert to IElement for indexing
        var element = observation.ToElement(_schemaProvider);

        // Act: Extract search indices
        var indices = _indexer.Extract(element);

        // Diagnostic Output: Show all extracted indices
        Console.WriteLine("=== ALL EXTRACTED SEARCH INDICES ===");
        foreach (var entry in indices)
        {
            Console.WriteLine($"Parameter: {entry.SearchParameter.Code} (Type: {entry.SearchParameter.Type})");
            Console.WriteLine($"  Value Type: {entry.Value.GetType().Name}");
            Console.WriteLine($"  Value: {entry.Value}");
            Console.WriteLine();
        }

        // Assert: Find the code-value-quantity composite index
        var codeValueQuantityEntry = indices
            .FirstOrDefault(i => i.SearchParameter.Code == "code-value-quantity");

        // DIAGNOSTIC CHECK 1: Is the search parameter found?
        Console.WriteLine("=== DIAGNOSTIC CHECK 1: Search Parameter Found ===");
        if (codeValueQuantityEntry == null)
        {
            Console.WriteLine("❌ FAILED: code-value-quantity search parameter NOT FOUND in extracted indices");
            Console.WriteLine("Available search parameters:");
            foreach (var entry in indices)
            {
                Console.WriteLine($"  - {entry.SearchParameter.Code} ({entry.SearchParameter.Type})");
            }

            // Check if search parameter definition exists
            var searchParamDef = _searchParamManager.GetSearchParameters("Observation")
                .FirstOrDefault(sp => sp.Code == "code-value-quantity");

            if (searchParamDef == null)
            {
                Console.WriteLine("❌ Search parameter definition NOT FOUND in schema");
            }
            else
            {
                Console.WriteLine($"✓ Search parameter definition EXISTS: {searchParamDef.Url}");
                Console.WriteLine($"  Expression: {searchParamDef.Expression}");
                Console.WriteLine($"  Type: {searchParamDef.Type}");
                Console.WriteLine($"  Components: {searchParamDef.Component?.Count ?? 0}");

                if (searchParamDef.Component != null)
                {
                    for (int i = 0; i < searchParamDef.Component.Count; i++)
                    {
                        var comp = searchParamDef.Component[i];
                        Console.WriteLine($"    Component {i}:");
                        Console.WriteLine($"      Definition: {comp.DefinitionUrl}");
                        Console.WriteLine($"      Expression: {comp.Expression}");
                        Console.WriteLine($"      Resolved: {comp.ResolvedSearchParameter?.Code ?? "(null)"}");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine("✓ code-value-quantity search parameter FOUND");
        }

        codeValueQuantityEntry.ShouldNotBeNull("code-value-quantity search parameter should be extracted");

        // DIAGNOSTIC CHECK 2: Is it a CompositeSearchValue?
        Console.WriteLine("\n=== DIAGNOSTIC CHECK 2: Value Type ===");
        Console.WriteLine($"Value Type: {codeValueQuantityEntry.Value.GetType().Name}");

        codeValueQuantityEntry.Value.ShouldBeOfType<CompositeSearchValue>(
            "code-value-quantity should produce CompositeSearchValue");

        var compositeValue = (CompositeSearchValue)codeValueQuantityEntry.Value;

        // DIAGNOSTIC CHECK 3: Component count
        Console.WriteLine("\n=== DIAGNOSTIC CHECK 3: Component Count ===");
        Console.WriteLine($"Component count: {compositeValue.Components.Count}");

        compositeValue.Components.Count.ShouldBe(2,
            "Composite should have 2 components (code + value)");

        // DIAGNOSTIC CHECK 4: Component 0 (code) should be TokenSearchValue
        Console.WriteLine("\n=== DIAGNOSTIC CHECK 4: Component 0 (Code) ===");
        var codeComponent = compositeValue.Components[0];
        Console.WriteLine($"Component 0 count: {codeComponent.Count}");

        codeComponent.ShouldNotBeEmpty("Code component should have values");

        foreach (var value in codeComponent)
        {
            Console.WriteLine($"  Value Type: {value.GetType().Name}");
            Console.WriteLine($"  Value: {value}");

            if (value is TokenSearchValue token)
            {
                Console.WriteLine($"    System: {token.System}");
                Console.WriteLine($"    Code: {token.Code}");
                Console.WriteLine($"    Text: {token.Text}");
            }
        }

        var codeToken = codeComponent[0];
        codeToken.ShouldNotBeNull("Code component should have at least one value");
        codeToken.ShouldBeOfType<TokenSearchValue>("Code component should be TokenSearchValue");

        var codeTokenValue = (TokenSearchValue)codeToken;
        codeTokenValue.System.ShouldBe("http://loinc.org", "Code system should be LOINC");
        codeTokenValue.Code.ShouldBe("9272-6", "Code should be APGAR 1-minute");

        // DIAGNOSTIC CHECK 5: Component 1 (value) should be QuantitySearchValue
        Console.WriteLine("\n=== DIAGNOSTIC CHECK 5: Component 1 (Value) ===");
        var valueComponent = compositeValue.Components[1];
        Console.WriteLine($"Component 1 count: {valueComponent.Count}");

        valueComponent.ShouldNotBeEmpty("Value component should have values");

        foreach (var value in valueComponent)
        {
            Console.WriteLine($"  Value Type: {value.GetType().Name}");
            Console.WriteLine($"  Value: {value}");

            if (value is QuantitySearchValue qsv)
            {
                Console.WriteLine($"    System: {qsv.System}");
                Console.WriteLine($"    Code: {qsv.Code}");
                Console.WriteLine($"    Low: {qsv.Low}");
                Console.WriteLine($"    High: {qsv.High}");
            }
        }

        var quantityValue = valueComponent[0];
        quantityValue.ShouldNotBeNull("Value component should have at least one value");
        quantityValue.ShouldBeOfType<QuantitySearchValue>("Value component should be QuantitySearchValue");

        var quantitySearchValue = (QuantitySearchValue)quantityValue;
        quantitySearchValue.System.ShouldBe("http://unitsofmeasure.org", "Quantity system should be UCUM");
        quantitySearchValue.Code.ShouldBe("{score}", "Quantity unit should be {score}");
        quantitySearchValue.Low.ShouldBe(10m, "Quantity value low should be 10");
        quantitySearchValue.High.ShouldBe(10m, "Quantity value high should be 10");

        Console.WriteLine("\n=== TEST PASSED ===");
        Console.WriteLine("Composite search indexing is working correctly!");
    }

    /// <summary>
    /// Diagnostic test to trace through the FHIRPath expression evaluation
    /// for the code-value-quantity composite search parameter.
    ///
    /// This helps identify if the issue is:
    /// 1. FHIRPath expression not evaluating correctly
    /// 2. Component expressions not extracting the right elements
    /// 3. Converters not being found for the element types
    /// </summary>
    [Fact]
    public void GivenObservationWithCodeAndValueQuantity_WhenEvaluatingFhirPath_ThenCorrectElementsExtracted()
    {
        // Arrange
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);

        // Get the search parameter definition
        var searchParamDef = _searchParamManager.GetSearchParameters("Observation")
            .FirstOrDefault(sp => sp.Code == "code-value-quantity");

        searchParamDef.ShouldNotBeNull("code-value-quantity search parameter should exist");

        Console.WriteLine("=== SEARCH PARAMETER DEFINITION ===");
        Console.WriteLine($"Code: {searchParamDef.Code}");
        Console.WriteLine($"Type: {searchParamDef.Type}");
        Console.WriteLine($"Expression: {searchParamDef.Expression}");
        Console.WriteLine($"Components: {searchParamDef.Component.Count}");

        // DIAGNOSTIC: Evaluate root expression
        Console.WriteLine("\n=== ROOT EXPRESSION EVALUATION ===");
        var rootElements = element.Select(searchParamDef.Expression);
        Console.WriteLine($"Root expression: {searchParamDef.Expression}");
        Console.WriteLine($"Root elements count: {rootElements.Count()}");

        foreach (var rootElement in rootElements)
        {
            Console.WriteLine($"  Root element type: {rootElement.InstanceType}");
        }

        rootElements.ShouldNotBeEmpty("Root expression should extract elements");

        // DIAGNOSTIC: Evaluate component 0 (code) expression
        Console.WriteLine("\n=== COMPONENT 0 (CODE) EXPRESSION EVALUATION ===");
        var component0 = searchParamDef.Component[0];
        Console.WriteLine($"Component 0 definition: {component0.DefinitionUrl}");
        Console.WriteLine($"Component 0 expression: {component0.Expression}");
        Console.WriteLine($"Component 0 resolved: {component0.ResolvedSearchParameter?.Code}");

        foreach (var rootElement in rootElements)
        {
            var codeElements = rootElement.Select(component0.Expression);
            Console.WriteLine($"Code elements count: {codeElements.Count()}");

            foreach (var codeElement in codeElements)
            {
                Console.WriteLine($"  Code element type: {codeElement.InstanceType}");
                Console.WriteLine($"  Code element value: {codeElement}");
            }
        }

        // DIAGNOSTIC: Evaluate component 1 (value) expression
        Console.WriteLine("\n=== COMPONENT 1 (VALUE) EXPRESSION EVALUATION ===");
        var component1 = searchParamDef.Component[1];
        Console.WriteLine($"Component 1 definition: {component1.DefinitionUrl}");
        Console.WriteLine($"Component 1 expression: {component1.Expression}");
        Console.WriteLine($"Component 1 resolved: {component1.ResolvedSearchParameter?.Code}");

        foreach (var rootElement in rootElements)
        {
            var valueElements = rootElement.Select(component1.Expression);
            Console.WriteLine($"Value elements count: {valueElements.Count()}");

            foreach (var valueElement in valueElements)
            {
                Console.WriteLine($"  Value element type: {valueElement.InstanceType}");
                Console.WriteLine($"  Value element: {valueElement}");

                // Check if we can extract the quantity properties
                if (valueElement.InstanceType == "Quantity")
                {
                    var quantityValue = valueElement.Scalar("value");
                    var quantitySystem = valueElement.Scalar("system");
                    var quantityCode = valueElement.Scalar("code");

                    Console.WriteLine($"    Quantity.value: {quantityValue}");
                    Console.WriteLine($"    Quantity.system: {quantitySystem}");
                    Console.WriteLine($"    Quantity.code: {quantityCode}");
                }
            }
        }

        Console.WriteLine("\n=== FHIRPATH EVALUATION COMPLETE ===");
    }

    /// <summary>
    /// Diagnostic test to verify converter registration and availability
    /// for composite component types.
    /// </summary>
    [Fact]
    public void GivenSearchIndexer_WhenCheckingConverters_ThenTokenAndQuantityConvertersRegistered()
    {
        // Arrange: This test ensures the converters are properly registered
        // We can't directly access the converter manager, but we can verify
        // through the indexing process

        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("test-code", "http://test.system", "Test")
            .WithQuantityValue(42, "mg", "http://unitsofmeasure.org")
            .WithSubject("Patient/test")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert: Verify that both Token and Quantity converters are working
        Console.WriteLine("=== CONVERTER VERIFICATION ===");

        var codeIndices = indices.Where(i => i.SearchParameter.Type == SearchParamType.Token);
        Console.WriteLine($"Token indices count: {codeIndices.Count()}");
        codeIndices.ShouldNotBeEmpty("Should extract token indices (proves TokenSearchValue converter works)");

        var valueIndices = indices.Where(i => i.SearchParameter.Code == "value-quantity");
        Console.WriteLine($"Quantity indices count: {valueIndices.Count()}");
        valueIndices.ShouldNotBeEmpty("Should extract quantity indices (proves QuantitySearchValue converter works)");

        Console.WriteLine("\n✓ Both converters are registered and working");
    }

    /// <summary>
    /// Diagnostic test to verify the SearchParameterExpressionParser correctly
    /// parses comparators for composite search parameters.
    /// </summary>
    [Theory]
    [InlineData("http://loinc.org|9272-6$lt15", SearchComparator.Lt, 15)]
    [InlineData("http://loinc.org|9272-6$le10", SearchComparator.Le, 10)]
    [InlineData("http://loinc.org|9272-6$gt15", SearchComparator.Gt, 15)]
    [InlineData("http://loinc.org|9272-6$ge20", SearchComparator.Ge, 20)]
    [InlineData("http://loinc.org|9272-6$10", SearchComparator.Eq, 10)]
    public void GivenCompositeSearchValue_WhenParsing_ThenComparatorIsParsedCorrectly(
        string queryValue, SearchComparator expectedComparator, decimal expectedValue)
    {
        // Arrange: Get the code-value-quantity search parameter
        var searchParam = _searchParamManager.GetSearchParameters("Observation")
            .FirstOrDefault(sp => sp.Code == "code-value-quantity");

        searchParam.ShouldNotBeNull("code-value-quantity should exist");

        // Verify component resolution
        searchParam.Component.ShouldNotBeNull();
        searchParam.Component.Count.ShouldBe(2);
        searchParam.Component[0].ResolvedSearchParameter.ShouldNotBeNull("Component 0 should be resolved");
        searchParam.Component[1].ResolvedSearchParameter.ShouldNotBeNull("Component 1 should be resolved");

        Console.WriteLine($"Search Parameter: {searchParam.Code}");
        Console.WriteLine($"Component 0: {searchParam.Component[0].ResolvedSearchParameter.Code} (Type: {searchParam.Component[0].ResolvedSearchParameter.Type})");
        Console.WriteLine($"Component 1: {searchParam.Component[1].ResolvedSearchParameter.Code} (Type: {searchParam.Component[1].ResolvedSearchParameter.Type})");

        // Create the expression parser
        var parser = new SearchParameterExpressionParser(
            new ReferenceSearchValueParser(_schemaProvider),
            _schemaProvider);

        // Act: Parse the composite search expression
        var expression = parser.Parse(searchParam, null, queryValue);

        // Assert: Check the expression structure
        expression.ShouldBeOfType<SearchParameterExpression>();
        var searchParamExpr = (SearchParameterExpression)expression;

        Console.WriteLine($"\nParsed Expression Tree:");
        PrintExpression(searchParamExpr.Expression, 0);

        // For a single value composite, the inner expression should be:
        // And(tokenExpr, quantityExpr)
        // For comparator queries, quantityExpr should have just 1 BinaryExpression
        // For equality queries, quantityExpr should have 2 BinaryExpressions (range)

        searchParamExpr.Expression.ShouldBeOfType<MultiaryExpression>();
        var andExpr = (MultiaryExpression)searchParamExpr.Expression;
        andExpr.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        andExpr.Expressions.Count.ShouldBe(2, "Should have 2 component expressions");

        // Second expression should be the quantity expression
        var quantityExpr = andExpr.Expressions[1];
        Console.WriteLine($"\nQuantity Expression Type: {quantityExpr.GetType().Name}");

        // Count BinaryExpressions with FieldName.Quantity
        var binaryExprs = new List<BinaryExpression>();
        CollectBinaryExpressions(quantityExpr, binaryExprs);

        Console.WriteLine($"\nQuantity BinaryExpressions count: {binaryExprs.Count}");
        foreach (var binaryExpr in binaryExprs)
        {
            Console.WriteLine($"  Op: {binaryExpr.BinaryOperator}, Value: {binaryExpr.Value}");
        }

        if (expectedComparator == SearchComparator.Eq)
        {
            // Equality creates a range (2 expressions)
            binaryExprs.Count.ShouldBe(2, "Eq comparator should create 2 BinaryExpressions (range)");
        }
        else
        {
            // Single comparator creates 1 expression
            binaryExprs.Count.ShouldBe(1, $"{expectedComparator} comparator should create 1 BinaryExpression");
            binaryExprs[0].Value.ShouldBe(expectedValue, $"Value should be {expectedValue}");
        }
    }

    private void CollectBinaryExpressions(Expression expr, List<BinaryExpression> results)
    {
        if (expr is BinaryExpression binary && binary.FieldName == FieldName.Quantity)
        {
            results.Add(binary);
        }
        else if (expr is MultiaryExpression multiary)
        {
            foreach (var child in multiary.Expressions)
            {
                CollectBinaryExpressions(child, results);
            }
        }
    }

    private void PrintExpression(Expression expr, int indent)
    {
        var prefix = new string(' ', indent * 2);
        switch (expr)
        {
            case SearchParameterExpression sp:
                Console.WriteLine($"{prefix}SearchParam({sp.Parameter.Code})");
                PrintExpression(sp.Expression, indent + 1);
                break;
            case MultiaryExpression m:
                Console.WriteLine($"{prefix}{m.MultiaryOperation}(");
                foreach (var child in m.Expressions)
                {
                    PrintExpression(child, indent + 1);
                }
                Console.WriteLine($"{prefix})");
                break;
            case BinaryExpression b:
                Console.WriteLine($"{prefix}Binary({b.FieldName}, {b.BinaryOperator}, {b.Value}, ComponentIndex={b.ComponentIndex})");
                break;
            case StringExpression s:
                Console.WriteLine($"{prefix}String({s.FieldName}, {s.StringOperator}, '{s.Value}', ComponentIndex={s.ComponentIndex})");
                break;
            default:
                Console.WriteLine($"{prefix}{expr.GetType().Name}");
                break;
        }
    }
}
