/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ISourceNavigator-based parser for SQL on FHIR v2 ViewDefinitions.
 * Builds an immutable expression tree with compiled FHIRPath for evaluation.
 * This is the ONLY parser needed - it goes directly from ISourceNavigator to ViewDefinitionExpression.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.SqlOnFhir.Expressions;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNode/ITypedElement migration pending

namespace Ignixa.SqlOnFhir.Parsing;

/// <summary>
/// Parses SQL on FHIR v2 ViewDefinition from ISourceNavigator into an immutable expression tree.
/// Uses ISourceNavigator for proper handling of choice types (value[x]) and polymorphism.
/// Compiles FHIRPath expressions during parsing for better performance.
/// This replaces both ViewDefinitionParser and ViewDefinitionModelParser with a single clean path.
/// </summary>
public static class ViewDefinitionExpressionParser
{
    private static readonly FhirPathParser Parser = new();

    /// <summary>
    /// Parses a ViewDefinition from an ISourceNavigator into an expression tree.
    /// </summary>
    /// <param name="viewNode">The ISourceNavigator containing the ViewDefinition JSON</param>
    /// <returns>An immutable ViewDefinitionExpression with compiled FHIRPath</returns>
    public static ViewDefinitionExpression Parse(ISourceNavigator viewNode)
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
    private static ImmutableArray<ConstantExpression> ParseConstants(ISourceNavigator viewNode)
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
            object? value = ExtractValue(constantNode, out var valueType);

            // Validate that a value was provided
            if (value == null)
            {
                throw new InvalidOperationException($"Constant '{name}' must have a value property (valueString, valueInteger, valueBoolean, etc.)");
            }

            builder.Add(new ConstantExpression(name, value, valueType));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses WHERE clauses from the ViewDefinition and compiles FHIRPath expressions.
    /// </summary>
    private static ImmutableArray<WhereExpression> ParseWhereClauses(ISourceNavigator viewNode)
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
            var expr = Parser.Parse(path);
            builder.Add(new WhereExpression(expr));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses SELECT groups from the ViewDefinition and compiles all FHIRPath expressions.
    /// </summary>
    private static ImmutableArray<SelectExpression> ParseSelectGroups(ISourceNavigator viewNode)
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
                ? Parser.Parse(forEachText)
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
                ? Parser.Parse(forEachOrNullText)
                : null;

            // Parse repeat - array of FHIRPath expressions
            var repeatNodes = selectNode.Children("repeat").ToList();
            var repeatBuilder = ImmutableArray.CreateBuilder<FhirPath.Expressions.Expression>(repeatNodes.Count);
            foreach (var repeatNode in repeatNodes)
            {
                var repeatPath = repeatNode.Text;
                if (!string.IsNullOrEmpty(repeatPath))
                {
                    repeatBuilder.Add(Parser.Parse(repeatPath));
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
    private static ImmutableArray<ColumnExpression> ParseColumns(ISourceNavigator selectNode)
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
            var path = Parser.Parse(pathText);

            var tagNodes = columnNode.Children("tag").ToList();
            var tags = ImmutableArray<(string Name, string Value)>.Empty;
            if (tagNodes.Count > 0)
            {
                var tagBuilder = ImmutableArray.CreateBuilder<(string Name, string Value)>(tagNodes.Count);
                foreach (var tagNode in tagNodes)
                {
                    var tagName = tagNode.Children("name").FirstOrDefault()?.Text;
                    if (string.IsNullOrEmpty(tagName))
                        throw new InvalidOperationException("Column tag 'name' must be a non-empty string");
                    var tagValue = tagNode.Children("value").FirstOrDefault()?.Text;
                    if (tagValue is null)
                        throw new InvalidOperationException("Column tag must have a 'value' property");
                    tagBuilder.Add((tagName, tagValue));
                }
                tags = tagBuilder.ToImmutable();
            }

            builder.Add(new ColumnExpression(
                Name: name,
                Path: path,
                Type: type,
                Collection: collection,
                Tags: tags
            ));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses nested select groups from a specific property ("select" or "unionAll").
    /// "select" creates Cartesian products, "unionAll" concatenates results.
    /// </summary>
    private static ImmutableArray<SelectExpression> ParseNestedSelectGroups(
        ISourceNavigator selectNode,
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
                ? Parser.Parse(forEachText)
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
                ? Parser.Parse(forEachOrNullText)
                : null;

            // Parse repeat - array of FHIRPath expressions
            var repeatNodes = nestedNode.Children("repeat").ToList();
            var repeatBuilder = ImmutableArray.CreateBuilder<FhirPath.Expressions.Expression>(repeatNodes.Count);
            foreach (var repeatNode in repeatNodes)
            {
                var repeatPath = repeatNode.Text;
                if (!string.IsNullOrEmpty(repeatPath))
                {
                    repeatBuilder.Add(Parser.Parse(repeatPath));
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
    /// Extracts the value and the declared FHIRPath type from a constant node's value[x] property.
    /// </summary>
    /// <param name="constantNode">The <c>constant</c> element.</param>
    /// <param name="valueType">
    /// Receives the FHIRPath type the declared suffix converts to, or <see langword="null"/> only when
    /// there is no suffix at all - a bare <c>value</c> property, or no <c>value[x]</c> child. A suffix
    /// this method does not enumerate yields <c>"string"</c>, not <see langword="null"/>; see
    /// <see cref="SystemTypeOf"/>.
    /// </param>
    /// <returns>The constant's value, or <see langword="null"/> when it has none.</returns>
    private static object? ExtractValue(ISourceNavigator constantNode, out string? valueType)
    {
        valueType = null;

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

            valueType = SystemTypeOf(typeSuffix);

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
    /// Maps a <c>value[x]</c> type suffix to the FHIRPath type the FHIR primitive converts to.
    /// </summary>
    /// <param name="typeSuffix">The suffix, with the <c>value</c> prefix already removed.</param>
    /// <returns>
    /// The FHIRPath type name. <see langword="null"/> only for the empty suffix - a bare <c>value</c>
    /// property with no type - which leaves the constant to be typed by inference as before. An
    /// unrecognised suffix is <em>not</em> null: it falls to <c>"string"</c>, because every FHIR
    /// primitive this switch does not name converts to System.String, and a suffix from a newer FHIR
    /// version is far more likely to be another of those than something else.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For well-formed input only the temporal suffixes change anything a caller could not already
    /// infer: the switch above hands back an <see cref="int"/>, <see cref="decimal"/> or
    /// <see cref="bool"/> for the numeric and boolean suffixes, and every remaining FHIR primitive -
    /// <c>code</c>, <c>uri</c>, <c>id</c>, <c>oid</c>, <c>uuid</c>, <c>url</c>, <c>canonical</c>,
    /// <c>markdown</c>, <c>base64Binary</c> - converts to System.String, which is what a bare
    /// <see cref="string"/> already types as. The four temporals are the ones whose type is
    /// unrecoverable from their CLR representation, so they are the ones a comparison against a resource
    /// element got wrong.
    /// </para>
    /// <para>
    /// Three cases fall outside "well-formed", and they do change: <c>ExtractValue</c> assigns the
    /// declared type before its numeric and boolean arms attempt <c>TryParse</c>, and those arms fall
    /// back to the raw text when the parse fails. So an unparseable <c>valueInteger</c>,
    /// <c>valuePositiveInt</c>, <c>valueUnsignedInt</c>, <c>valueDecimal</c> or <c>valueBoolean</c> now
    /// carries a numeric or boolean declared type over a <see cref="string"/> value, where inference
    /// previously said <c>"string"</c>. This is deliberate and is the better answer:
    /// <c>ValueOrdering.IsNumericValued</c> exists precisely to handle a string value under a numeric
    /// declared type, which is the designed representation for a FHIR decimal outside
    /// <see cref="decimal"/>'s range. It is reachable from conformant input - FHIR's decimal regex
    /// permits exponent notation and <c>decimal.TryParse("1e5")</c> is <see langword="false"/> under
    /// default <c>NumberStyles</c> - so <c>"valueDecimal": "1e5"</c> lands here.
    /// </para>
    /// <para>
    /// They are deliberately named for the type, not returned verbatim from the suffix: a constant is a
    /// System value, and this spelling is the one the evaluator's own
    /// <c>FhirPathEvaluator.GetFhirPathTypeName</c> uses for a temporal, so the two producers of a
    /// System temporal agree.
    /// </para>
    /// </remarks>
    private static string? SystemTypeOf(string typeSuffix) => typeSuffix switch
    {
        "Date" => "date",
        "DateTime" => "dateTime",
        "Instant" => "instant",
        "Time" => "time",
        "Integer" or "PositiveInt" or "UnsignedInt" => "integer",
        "Decimal" => "decimal",
        "Boolean" => "boolean",
        "" => null,
        _ => "string"
    };

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
        // Exclude special predefined variables like 'resource', 'rootResource', 'context', 'ucum', 'sct', 'loinc', 'vs-*', 'ext-*', 'rowIndex'
        var predefinedVariables = new HashSet<string>(StringComparer.Ordinal)
        {
            "context", "resource", "rootResource", "ucum", "sct", "loinc", "rowIndex"
        };

        foreach (var varName in referencedVariables)
        {
            // Skip predefined variables and the vs-/ext- prefix families. The FHIR profile of FHIRPath
            // defines both (%vs-[name] -> ValueSet URI, %ext-[name] -> extension URI) the same way, so
            // exempting only "vs-" here was an asymmetry: with the tokenizer fixed to lex the bare
            // spelling as a single ExternalConstant (issue #438), %ext-x would otherwise start being
            // rejected as an undefined constant while %vs-x passed.
            if (predefinedVariables.Contains(varName)
                || varName.StartsWith("vs-", StringComparison.Ordinal)
                || varName.StartsWith("ext-", StringComparison.Ordinal))
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
