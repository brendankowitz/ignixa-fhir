/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ISourceNode-based parser for SQL on FHIR v2 ViewDefinitions.
 * Builds an immutable expression tree with compiled FHIRPath for evaluation.
 * This is the ONLY parser needed - it goes directly from ISourceNode to ViewDefinitionExpression.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath;
using Ignixa.Abstractions;
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

        // Validate that all referenced constants are defined
        ValidateConstantReferences(constants, where, select);

        // Validate that WHERE clauses evaluate to boolean expressions
        ValidateWhereClausesReturnBoolean(where);

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
            // Validate that forEach is a string, not a number or other type
            var forEachNode = selectNode.Children("forEach").FirstOrDefault();
            string? forEachText = null;
            if (forEachNode != null)
            {
                forEachText = forEachNode.Text;
                // Check if the text looks like a number (invalid type for forEach)
                if (!string.IsNullOrEmpty(forEachText) && int.TryParse(forEachText, out _))
                {
                    throw new InvalidOperationException(
                        "forEach must be a FHIRPath string expression, not a number or other primitive type");
                }
            }

            var forEach = !string.IsNullOrEmpty(forEachText)
                ? _compiler.Parse(forEachText)
                : null;

            var forEachOrNullNode = selectNode.Children("forEachOrNull").FirstOrDefault();
            string? forEachOrNullText = null;
            if (forEachOrNullNode != null)
            {
                forEachOrNullText = forEachOrNullNode.Text;
                // Check if the text looks like a number (invalid type for forEachOrNull)
                if (!string.IsNullOrEmpty(forEachOrNullText) && int.TryParse(forEachOrNullText, out _))
                {
                    throw new InvalidOperationException(
                        "forEachOrNull must be a FHIRPath string expression, not a number or other primitive type");
                }
            }

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
            // Validate that forEach is a string, not a number or other type
            var forEachNode = nestedNode.Children("forEach").FirstOrDefault();
            string? forEachText = null;
            if (forEachNode != null)
            {
                forEachText = forEachNode.Text;
                // Check if the text looks like a number (invalid type for forEach)
                if (!string.IsNullOrEmpty(forEachText) && int.TryParse(forEachText, out _))
                {
                    throw new InvalidOperationException(
                        "forEach must be a FHIRPath string expression, not a number or other primitive type");
                }
            }

            var forEach = !string.IsNullOrEmpty(forEachText)
                ? _compiler.Parse(forEachText)
                : null;

            var forEachOrNullNode = nestedNode.Children("forEachOrNull").FirstOrDefault();
            string? forEachOrNullText = null;
            if (forEachOrNullNode != null)
            {
                forEachOrNullText = forEachOrNullNode.Text;
                // Check if the text looks like a number (invalid type for forEachOrNull)
                if (!string.IsNullOrEmpty(forEachOrNullText) && int.TryParse(forEachOrNullText, out _))
                {
                    throw new InvalidOperationException(
                        "forEachOrNull must be a FHIRPath string expression, not a number or other primitive type");
                }
            }

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

        // Get column names from first SELECT (recursively handle nested unionAll)
        var firstColumns = GetEffectiveColumns(unionAllGroups[0]);

        // Validate all subsequent SELECTs have same columns in same order
        for (int i = 1; i < unionAllGroups.Length; i++)
        {
            var currentColumns = GetEffectiveColumns(unionAllGroups[i]);

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

    /// <summary>
    /// Gets the effective column names that a SelectExpression produces.
    /// If the select has a nested unionAll, the columns come from the unionAll branches.
    /// Otherwise, returns the direct columns.
    /// </summary>
    private static List<string> GetEffectiveColumns(SelectExpression select)
    {
        // If this select has a nested unionAll, the columns come from the unionAll branches
        if (select.UnionAll.Length > 0)
        {
            // Recursively get columns from first branch of nested unionAll
            // (All branches should have same columns due to recursive validation)
            return GetEffectiveColumns(select.UnionAll[0]);
        }

        // Otherwise, return the direct columns
        return select.Columns.Select(c => c.Name).ToList();
    }

    /// <summary>
    /// Validates that all constant references in FHIRPath expressions are defined in the ViewDefinition.
    /// Per SQL on FHIR v2 spec, accessing undefined constants should throw an error.
    /// </summary>
    private static void ValidateConstantReferences(
        ImmutableArray<ConstantExpression> constants,
        ImmutableArray<WhereExpression> whereClauses,
        ImmutableArray<SelectExpression> selectGroups)
    {
        // Build set of defined constant names
        var definedConstants = constants.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        // Collect all variable references from all FHIRPath expressions
        var referencedVariables = new HashSet<string>(StringComparer.Ordinal);

        // Check WHERE clauses
        foreach (var whereClause in whereClauses)
        {
            CollectVariableReferences(whereClause.Filter, referencedVariables);
        }

        // Check SELECT groups
        foreach (var selectGroup in selectGroups)
        {
            CollectVariableReferencesFromSelect(selectGroup, referencedVariables);
        }

        // Find any referenced variables that are not defined constants
        // Exclude special predefined variables like 'resource', 'rootResource', 'context', 'ucum', 'sct', 'loinc', 'vs-*'
        var predefinedVariables = new HashSet<string>(StringComparer.Ordinal)
        {
            "context", "resource", "rootResource", "ucum", "sct", "loinc"
        };

        foreach (var varName in referencedVariables)
        {
            // Skip predefined variables and VS-* variables
            if (predefinedVariables.Contains(varName) || varName.StartsWith("vs-", StringComparison.Ordinal))
            {
                continue;
            }

            // Check if it's a defined constant
            if (!definedConstants.Contains(varName))
            {
                throw new InvalidOperationException(
                    $"ViewDefinition references undefined constant '%{varName}'. " +
                    $"Constants must be defined in the 'constant' array before use.");
            }
        }
    }

    /// <summary>
    /// Recursively collects all variable references from a FHIRPath expression tree.
    /// </summary>
    private static void CollectVariableReferences(FhirPath.Expressions.Expression expr, HashSet<string> variables)
    {
        if (expr == null)
            return;

        switch (expr)
        {
            case FhirPath.Expressions.VariableRefExpression varRef:
                variables.Add(varRef.Name);
                break;

            case FhirPath.Expressions.FunctionCallExpression funcCall:
                if (funcCall.Focus != null)
                    CollectVariableReferences(funcCall.Focus, variables);
                foreach (var arg in funcCall.Arguments)
                    CollectVariableReferences(arg, variables);
                break;

            case FhirPath.Expressions.ParenthesizedExpression paren:
                CollectVariableReferences(paren.InnerExpression, variables);
                break;

            // Other expression types (constants, identifiers, etc.) don't contain variable references
        }
    }

    /// <summary>
    /// Collects variable references from all FHIRPath expressions in a SELECT group (recursive).
    /// </summary>
    private static void CollectVariableReferencesFromSelect(SelectExpression select, HashSet<string> variables)
    {
        // Check forEach and forEachOrNull
        if (select.ForEach != null)
            CollectVariableReferences(select.ForEach, variables);
        if (select.ForEachOrNull != null)
            CollectVariableReferences(select.ForEachOrNull, variables);

        // Check repeat paths
        foreach (var repeatPath in select.Repeat)
        {
            CollectVariableReferences(repeatPath, variables);
        }

        // Check columns
        foreach (var column in select.Columns)
        {
            CollectVariableReferences(column.Path, variables);
        }

        // Recursively check nested selects
        foreach (var nestedSelect in select.NestedSelect)
        {
            CollectVariableReferencesFromSelect(nestedSelect, variables);
        }

        // Recursively check unionAll groups
        foreach (var unionAllGroup in select.UnionAll)
        {
            CollectVariableReferencesFromSelect(unionAllGroup, variables);
        }
    }

    /// <summary>
    /// Validates that WHERE clauses evaluate to boolean expressions.
    /// Per SQL on FHIR v2 spec, WHERE clause paths must resolve to boolean values.
    /// Simple validation: check if the path expression contains common boolean operators or ends with known boolean paths.
    /// </summary>
    private static void ValidateWhereClausesReturnBoolean(ImmutableArray<WhereExpression> whereClauses)
    {
        foreach (var whereClause in whereClauses)
        {
            var expr = whereClause.Filter;
            if (!LooksLikeBoolean(expr))
            {
                throw new InvalidOperationException(
                    $"WHERE clause path '{expr}' must evaluate to a boolean value. " +
                    $"Use comparison operators (=, !=, <, >, etc.) or boolean functions (exists(), empty(), etc.)");
            }
        }
    }

    /// <summary>
    /// Heuristic check if a FHIRPath expression likely returns a boolean.
    /// Returns false if the expression is a simple path that would return a complex type (like "name.family").
    /// Returns true if the expression contains boolean operators or functions.
    /// </summary>
    private static bool LooksLikeBoolean(FhirPath.Expressions.Expression expr)
    {
        // Check the expression type to determine if it's likely to return boolean
        // Note: Order matters! Check most specific types first (ChildExpression before FunctionCallExpression)
        return expr switch
        {
            // Simple identifiers or child access without operators are NOT boolean (e.g., "name.family")
            // These must be checked before FunctionCallExpression since ChildExpression extends FunctionCallExpression
            FhirPath.Expressions.ChildExpression => false,
            FhirPath.Expressions.IdentifierExpression => false,

            // Function calls that are likely boolean operations
            FhirPath.Expressions.FunctionCallExpression funcCall => IsLikelyBooleanFunction(funcCall),

            // Parenthesized expressions - check inner
            FhirPath.Expressions.ParenthesizedExpression paren => LooksLikeBoolean(paren.InnerExpression),

            // Literal booleans are fine
            FhirPath.Expressions.ConstantExpression constant => constant.Value is bool,

            // Default: allow other complex expressions (they might be boolean)
            _ => true
        };
    }

    /// <summary>
    /// Checks if a function call is likely to return a boolean.
    /// </summary>
    private static bool IsLikelyBooleanFunction(FhirPath.Expressions.FunctionCallExpression funcCall)
    {
        // List of known boolean-returning functions
        var booleanFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exists", "empty", "all", "allTrue", "anyTrue", "allFalse", "anyFalse",
            "subsetOf", "supersetOf", "isDistinct", "hasValue", "matches"
        };

        // Comparison operators in FHIRPath
        var comparisonOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "=", "!=", "~", "!~", "<", "<=", ">", ">=", "and", "or", "xor", "implies", "not"
        };

        var funcName = funcCall.FunctionName;

        // Check if it's a boolean function or operator
        if (booleanFunctions.Contains(funcName) || comparisonOperators.Contains(funcName))
        {
            return true;
        }

        // If it's a method call on something (e.g., "Patient.active" or "name.exists()"), check recursively
        // For simple child access without boolean operations, return false
        if (funcCall.Focus != null)
        {
            // If focus is a simple path and this is just accessing it, not boolean
            if (funcCall.Focus is FhirPath.Expressions.ChildExpression ||
                funcCall.Focus is FhirPath.Expressions.IdentifierExpression)
            {
                // Unless this function itself is a boolean function
                return booleanFunctions.Contains(funcName) || comparisonOperators.Contains(funcName);
            }
        }

        // Default: assume it might be boolean (to avoid false positives)
        return true;
    }
}
