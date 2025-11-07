/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ISourceNode-based parser for SQL on FHIR v2 ViewDefinitions.
 * Builds an immutable expression tree with compiled FHIRPath for evaluation.
 * This is the ONLY parser needed - it goes directly from ISourceNode to ViewDefinitionExpression.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath;
using Ignixa.Serialization.Abstractions;
using Ignixa.SqlOnFhir.Expressions;

namespace Ignixa.SqlOnFhir.Parsing;

/// <summary>
/// Parses SQL on FHIR v2 ViewDefinition from ISourceNode into an immutable expression tree.
/// Uses ISourceNode for proper handling of choice types (value[x]) and polymorphism.
/// Compiles FHIRPath expressions during parsing for better performance.
/// This replaces both ViewDefinitionParser and ViewDefinitionModelParser with a single clean path.
/// </summary>
public static class ViewDefinitionExpressionParser
{
    private static readonly FhirPathCompiler _compiler = new();

    /// <summary>
    /// Parses a ViewDefinition from an ISourceNode into an expression tree.
    /// </summary>
    /// <param name="viewNode">The ISourceNode containing the ViewDefinition JSON</param>
    /// <returns>An immutable ViewDefinitionExpression with compiled FHIRPath</returns>
    public static ViewDefinitionExpression Parse(ISourceNode viewNode)
    {
        ArgumentNullException.ThrowIfNull(viewNode);

        var resource = viewNode.Children("resource").FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("ViewDefinition must have a 'resource' property");

        var status = viewNode.Children("status").FirstOrDefault()?.Text;

        var constants = ParseConstants(viewNode);
        var where = ParseWhereClauses(viewNode);
        var select = ParseSelectGroups(viewNode);

        return new ViewDefinitionExpression(
            Resource: resource,
            Status: status,
            Constants: constants,
            Where: where,
            Select: select
        );
    }

    /// <summary>
    /// Parses constant definitions from the ViewDefinition.
    /// </summary>
    private static ImmutableArray<ConstantExpression> ParseConstants(ISourceNode viewNode)
    {
        var constantNodes = viewNode.Children("constant").ToList();
        if (constantNodes.Count == 0)
        {
            return ImmutableArray<ConstantExpression>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ConstantExpression>(constantNodes.Count);

        foreach (var constantNode in constantNodes)
        {
            var name = constantNode.Children("name").FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Constant must have a 'name' property");

            // Extract value from value[x] properties
            object? value = ExtractValue(constantNode);

            // Validate that a value was provided
            if (value == null)
            {
                throw new InvalidOperationException($"Constant '{name}' must have a value property (valueString, valueInteger, valueBoolean, etc.)");
            }

            builder.Add(new ConstantExpression(name, value));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses WHERE clauses from the ViewDefinition and compiles FHIRPath expressions.
    /// </summary>
    private static ImmutableArray<WhereExpression> ParseWhereClauses(ISourceNode viewNode)
    {
        var whereNodes = viewNode.Children("where").ToList();
        if (whereNodes.Count == 0)
        {
            return ImmutableArray<WhereExpression>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<WhereExpression>(whereNodes.Count);

        foreach (var whereNode in whereNodes)
        {
            var path = whereNode.Children("path").FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("WHERE clause must have a 'path' property");

            // Compile FHIRPath expression once during parsing
            var expr = _compiler.Parse(path);
            builder.Add(new WhereExpression(expr));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses SELECT groups from the ViewDefinition and compiles all FHIRPath expressions.
    /// </summary>
    private static ImmutableArray<SelectExpression> ParseSelectGroups(ISourceNode viewNode)
    {
        var selectNodes = viewNode.Children("select").ToList();
        if (selectNodes.Count == 0)
        {
            return ImmutableArray<SelectExpression>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SelectExpression>(selectNodes.Count);

        foreach (var selectNode in selectNodes)
        {
            // Parse forEach and forEachOrNull
            var forEachText = selectNode.Children("forEach").FirstOrDefault()?.Text;
            var forEach = !string.IsNullOrEmpty(forEachText)
                ? _compiler.Parse(forEachText)
                : null;

            var forEachOrNullText = selectNode.Children("forEachOrNull").FirstOrDefault()?.Text;
            var forEachOrNull = !string.IsNullOrEmpty(forEachOrNullText)
                ? _compiler.Parse(forEachOrNullText)
                : null;

            // Parse repeat - array of FHIRPath expressions
            var repeatNodes = selectNode.Children("repeat").ToList();
            var repeatBuilder = ImmutableArray.CreateBuilder<FhirPath.Expressions.Expression>(repeatNodes.Count);
            foreach (var repeatNode in repeatNodes)
            {
                var repeatPath = repeatNode.Text;
                if (!string.IsNullOrEmpty(repeatPath))
                {
                    repeatBuilder.Add(_compiler.Parse(repeatPath));
                }
            }
            var repeat = repeatBuilder.ToImmutable();

            // Parse columns
            var columns = ParseColumns(selectNode);

            // Parse nested select groups separately by property name
            var nestedSelects = ParseNestedSelectGroups(selectNode, "select");
            var unionAllGroups = ParseNestedSelectGroups(selectNode, "unionAll");

            // Per SQL on FHIR v2 spec Section 3.2.6: All SELECT expressions in unionAll
            // must have same column names in same order
            ValidateUnionAllColumns(unionAllGroups);

            builder.Add(new SelectExpression(forEach, forEachOrNull, repeat, columns, nestedSelects, unionAllGroups));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses column definitions from a SELECT group and compiles FHIRPath path expressions.
    /// </summary>
    private static ImmutableArray<ColumnExpression> ParseColumns(ISourceNode selectNode)
    {
        var columnNodes = selectNode.Children("column").ToList();
        if (columnNodes.Count == 0)
        {
            return ImmutableArray<ColumnExpression>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ColumnExpression>(columnNodes.Count);

        foreach (var columnNode in columnNodes)
        {
            var name = columnNode.Children("name").FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Column must have a 'name' property");

            var pathText = columnNode.Children("path").FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Column must have a 'path' property");

            var type = columnNode.Children("type").FirstOrDefault()?.Text;

            var collectionText = columnNode.Children("collection").FirstOrDefault()?.Text;
            var collection = bool.TryParse(collectionText, out var collectionValue) && collectionValue;

            // Compile FHIRPath expression once during parsing
            var path = _compiler.Parse(pathText);

            builder.Add(new ColumnExpression(
                Name: name,
                Path: path,
                Type: type,
                Collection: collection
            ));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses nested select groups from a specific property ("select" or "unionAll").
    /// "select" creates Cartesian products, "unionAll" concatenates results.
    /// </summary>
    private static ImmutableArray<SelectExpression> ParseNestedSelectGroups(
        ISourceNode selectNode,
        string propertyName)
    {
        var nestedNodes = selectNode.Children(propertyName).ToList();

        if (nestedNodes.Count == 0)
        {
            return ImmutableArray<SelectExpression>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SelectExpression>(nestedNodes.Count);

        foreach (var nestedNode in nestedNodes)
        {
            // Recursively parse nested select groups (same structure as top-level select)
            var forEachText = nestedNode.Children("forEach").FirstOrDefault()?.Text;
            var forEach = !string.IsNullOrEmpty(forEachText)
                ? _compiler.Parse(forEachText)
                : null;

            var forEachOrNullText = nestedNode.Children("forEachOrNull").FirstOrDefault()?.Text;
            var forEachOrNull = !string.IsNullOrEmpty(forEachOrNullText)
                ? _compiler.Parse(forEachOrNullText)
                : null;

            // Parse repeat - array of FHIRPath expressions
            var repeatNodes = nestedNode.Children("repeat").ToList();
            var repeatBuilder = ImmutableArray.CreateBuilder<FhirPath.Expressions.Expression>(repeatNodes.Count);
            foreach (var repeatNode in repeatNodes)
            {
                var repeatPath = repeatNode.Text;
                if (!string.IsNullOrEmpty(repeatPath))
                {
                    repeatBuilder.Add(_compiler.Parse(repeatPath));
                }
            }
            var repeat = repeatBuilder.ToImmutable();

            var columns = ParseColumns(nestedNode);

            // Recursively parse both "select" and "unionAll" at deeper levels
            var deeperNestedSelects = ParseNestedSelectGroups(nestedNode, "select");
            var deeperUnionAll = ParseNestedSelectGroups(nestedNode, "unionAll");

            builder.Add(new SelectExpression(
                forEach,
                forEachOrNull,
                repeat,
                columns,
                deeperNestedSelects,
                deeperUnionAll));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts the value from a constant node's value[x] property using choice type wildcard.
    /// </summary>
    private static object? ExtractValue(ISourceNode constantNode)
    {
        // Use choice type wildcard to match any value[x] property
        var valueNode = constantNode.Children("value*").FirstOrDefault();
        if (valueNode == null)
            return null;

        var text = valueNode.Text;
        if (string.IsNullOrEmpty(text))
            return null;

        // Try to parse based on the property name suffix
        var propertyName = valueNode.Name;
        if (propertyName.StartsWith("value", StringComparison.Ordinal))
        {
            var typeSuffix = propertyName.Substring(5); // Remove "value" prefix

            return typeSuffix switch
            {
                "Integer" or "PositiveInt" or "UnsignedInt" => int.TryParse(text, out var intValue) ? intValue : text,
                "Decimal" => decimal.TryParse(text, out var decimalValue) ? decimalValue : text,
                "Boolean" => bool.TryParse(text, out var boolValue) ? boolValue : text,
                // All other types (string, date, dateTime, time, instant, code, id, uri, url, oid, uuid, etc.)
                _ => text
            };
        }

        return text;
    }

    /// <summary>
    /// Validates that all SELECT expressions in a unionAll have the same column names in the same order.
    /// Per SQL on FHIR v2 Specification Section 3.2.6.
    /// </summary>
    private static void ValidateUnionAllColumns(ImmutableArray<SelectExpression> unionAllGroups)
    {
        if (unionAllGroups.Length <= 1)
        {
            return; // Nothing to validate
        }

        // Get column names from first SELECT
        var firstColumns = unionAllGroups[0].Columns.Select(c => c.Name).ToList();

        // Validate all subsequent SELECTs have same columns in same order
        for (int i = 1; i < unionAllGroups.Length; i++)
        {
            var currentColumns = unionAllGroups[i].Columns.Select(c => c.Name).ToList();

            if (!firstColumns.SequenceEqual(currentColumns))
            {
                var firstColumnList = string.Join(", ", firstColumns);
                var currentColumnList = string.Join(", ", currentColumns);
                throw new InvalidOperationException(
                    $"All SELECT expressions in unionAll must have the same columns in the same order. " +
                    $"First SELECT has columns: [{firstColumnList}], but SELECT #{i + 1} has columns: [{currentColumnList}]");
            }
        }
    }
}
