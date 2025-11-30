/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Visitor implementation for evaluating SQL on FHIR ViewDefinition expressions.
 * Pure visitor pattern - separates traversal logic from expression structure.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Expressions;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNode migration pending

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Visitor that evaluates ViewDefinition expressions against FHIR resources.
/// Implements the visitor pattern for clean separation of concerns.
/// </summary>
public class SqlOnFhirEvaluationVisitor : ISqlOnFhirExpressionVisitor<object?>
{
    private readonly FhirPathEvaluator _evaluator;
    private IElement? _currentResource;
    private EvaluationContext? _currentContext;

    public SqlOnFhirEvaluationVisitor()
    {
        _evaluator = new FhirPathEvaluator();
    }

    /// <summary>
    /// Evaluates a ViewDefinition expression against a FHIR resource.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> Evaluate(ViewDefinitionExpression viewDef, IElement resource)
    {
        _currentResource = resource;
        _currentContext = CreateEvaluationContext(viewDef);

        // Set the root resource for SQL on FHIR v2 functions like getResourceKey()
        _currentContext.RootResource = resource;

        return (IEnumerable<Dictionary<string, object?>>)viewDef.Accept(this)!;
    }

    /// <summary>
    /// Visits a ViewDefinition expression and returns rows.
    /// </summary>
    public object? Visit(ViewDefinitionExpression node)
    {
        ArgumentNullException.ThrowIfNull(_currentResource);

        // Note: ViewDefinition.status is metadata about the ViewDefinition itself (draft/active/retired),
        // not a filter on resources. Resource filtering is done via WHERE clauses.

        // Apply WHERE filters
        foreach (var whereExpr in node.Where)
        {
            var result = (bool)whereExpr.Accept(this)!;
            if (!result)
            {
                return Enumerable.Empty<Dictionary<string, object?>>();
            }
        }

        // Evaluate SELECT groups with proper row merging
        if (node.Select.IsEmpty)
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }

        // Start with first SELECT group
        var currentRows = ((IEnumerable<Dictionary<string, object?>>)node.Select[0].Accept(this)!).ToList();

        // Merge subsequent SELECT groups
        for (int i = 1; i < node.Select.Length; i++)
        {
            var selectExpr = node.Select[i];

            // If no forEach/forEachOrNull/repeat/unionAll, just add columns to existing rows
            if (selectExpr.ForEach == null && selectExpr.ForEachOrNull == null && selectExpr.Repeat.IsEmpty && selectExpr.UnionAll.IsEmpty)
            {
                foreach (var row in currentRows)
                {
                    var columns = EvaluateColumns(selectExpr.Columns);
                    foreach (var kvp in columns)
                    {
                        row[kvp.Key] = kvp.Value;
                    }
                }
            }
            else
            {
                // Has forEach/forEachOrNull/repeat/unionAll: create Cartesian product
                var selectRows = ((IEnumerable<Dictionary<string, object?>>)selectExpr.Accept(this)!).ToList();

                if (selectRows.Count == 0)
                {
                    if (selectExpr.ForEachOrNull != null)
                    {
                        // forEachOrNull with empty result: add null columns
                        foreach (var row in currentRows)
                        {
                            foreach (var column in selectExpr.Columns)
                            {
                                row[column.Name] = null;
                            }
                        }
                    }
                    else
                    {
                        // forEach with empty result: remove all rows
                        // The Cartesian product of any set with an empty set is empty
                        currentRows = new List<Dictionary<string, object?>>();
                    }
                }
                else
                {
                    // Create Cartesian product
                    // Note: The later SELECT (selectRows) varies in the outer loop,
                    // earlier SELECTs (currentRows) vary in the inner loop
                    var newRows = new List<Dictionary<string, object?>>();
                    foreach (var selectRow in selectRows)
                    {
                        foreach (var currentRow in currentRows)
                        {
                            var mergedRow = new Dictionary<string, object?>(currentRow);
                            foreach (var kvp in selectRow)
                            {
                                mergedRow[kvp.Key] = kvp.Value;
                            }
                            newRows.Add(mergedRow);
                        }
                    }
                    currentRows = newRows;
                }
            }
        }

        return currentRows;
    }

    /// <summary>
    /// Visits a SELECT expression and returns rows.
    /// </summary>
    public object? Visit(SelectExpression node)
    {
        ArgumentNullException.ThrowIfNull(_currentResource);

        // If no forEach/forEachOrNull/repeat, evaluate columns directly against the resource
        if (node.ForEach == null && node.ForEachOrNull == null && node.Repeat.IsEmpty)
        {
            var row = EvaluateColumns(node.Columns);

            // Process nested SELECT groups (Cartesian product)
            var rowsWithSelect = ProcessNestedSelectsCartesian(new[] { row }, node.NestedSelect);

            // Process UnionAll groups (Concatenation)
            var finalRows = ProcessUnionAllConcat(rowsWithSelect, node.UnionAll);
            return finalRows;
        }

        // repeat: recursively traverse paths and collect all results
        if (!node.Repeat.IsEmpty)
        {
            var allItems = RecursivelyCollectItems(_currentResource, node.Repeat);

            var repeatRows = new List<Dictionary<string, object?>>();

            foreach (var item in allItems)
            {
                // Temporarily switch context to the repeat item
                var previousResource = _currentResource;
                _currentResource = item;

                var row = EvaluateColumns(node.Columns);

                // Process nested SELECT and UnionAll WITHIN the repeat context
                var rowsForThisItem = ProcessNestedSelectsCartesian(new[] { row }, node.NestedSelect);
                rowsForThisItem = ProcessUnionAllConcat(rowsForThisItem, node.UnionAll);

                repeatRows.AddRange(rowsForThisItem);

                _currentResource = previousResource;
            }

            return repeatRows;
        }

        // forEach: unnest collection
        var forEachExpr = node.ForEach ?? node.ForEachOrNull!;
        var items = _evaluator.Evaluate(_currentResource, forEachExpr, _currentContext!);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var item in items)
        {
            // Temporarily switch context to the forEach item
            var previousResource = _currentResource;
            _currentResource = item;

            var row = EvaluateColumns(node.Columns);

            // Process nested SELECT and UnionAll WITHIN the forEach context
            // This ensures the context is correct for evaluating nested expressions
            var rowsForThisItem = ProcessNestedSelectsCartesian(new[] { row }, node.NestedSelect);
            rowsForThisItem = ProcessUnionAllConcat(rowsForThisItem, node.UnionAll);

            rows.AddRange(rowsForThisItem);

            _currentResource = previousResource;
        }

        // forEachOrNull: if no items, return a single row with nulls
        if (node.ForEachOrNull != null && rows.Count == 0)
        {
            var nullRow = new Dictionary<string, object?>();

            // Add null for columns in this SELECT
            foreach (var column in node.Columns)
            {
                nullRow[column.Name] = null;
            }

            // Add null for all columns in unionAll groups (don't evaluate, paths are relative to forEach context)
            foreach (var unionAllGroup in node.UnionAll)
            {
                foreach (var column in unionAllGroup.Columns)
                {
                    nullRow[column.Name] = null;
                }
            }

            // Process nested SELECT for the null row (but not unionAll - already handled above)
            var nullRowProcessed = ProcessNestedSelectsCartesian(new[] { nullRow }, node.NestedSelect);

            rows.AddRange(nullRowProcessed);
        }

        return rows;
    }

    /// <summary>
    /// Visits a column expression and returns the evaluated value.
    /// </summary>
    public object? Visit(ColumnExpression node)
    {
        ArgumentNullException.ThrowIfNull(_currentResource);

        // FHIRPath expression is already compiled - just evaluate it!
        var results = _evaluator.Evaluate(_currentResource, node.Path, _currentContext!);
        var resultsList = results.ToList();

        // If collection=true, return all values as array
        if (node.Collection)
        {
            var values = resultsList.Select(ExtractValue).Select(v => ConvertToSqlType(v, node.Type)).ToArray();
            return FormatArrayAsJson(values);
        }

        // Otherwise, return first value only (SQL on FHIR default behavior)
        // Per SQL on FHIR v2 spec Section 3.2.4: when collection=false, path MUST return at most one value
        if (resultsList.Count > 1)
        {
            throw new InvalidOperationException(
                $"Column '{node.Name}' has collection=false but FHIRPath expression '{node.Path}' returned {resultsList.Count} values. " +
                "Either set collection=true or ensure the path returns at most one value.");
        }

        var firstResult = resultsList.FirstOrDefault();
        var rawValue = ExtractValue(firstResult);

        // Per FHIRPath N1 spec and SQL on FHIR v2 spec:
        // - Empty FHIRPath collection → SQL null (including for boolean columns)
        // - FHIRPath comparison operators return empty when operand is missing
        var converted = ConvertToSqlType(rawValue, node.Type);
        return converted;
    }

    /// <summary>
    /// Visits a WHERE expression and returns true if the filter passes.
    /// </summary>
    public object? Visit(WhereExpression node)
    {
        ArgumentNullException.ThrowIfNull(_currentResource);

        // FHIRPath expression is already compiled - just evaluate it!
        var result = _evaluator.Evaluate(_currentResource, node.Filter, _currentContext!);

        // WHERE clause must evaluate to true
        return IsTrue(result);
    }

    /// <summary>
    /// Visits a constant expression (not directly evaluated, used in context setup).
    /// </summary>
    public object? Visit(ConstantExpression node)
    {
        return node.Value;
    }

    /// <summary>
    /// Evaluates all columns in a SELECT group against the current context element.
    /// </summary>
    private Dictionary<string, object?> EvaluateColumns(ImmutableArray<ColumnExpression> columns)
    {
        var row = new Dictionary<string, object?>();

        foreach (var column in columns)
        {
            var value = column.Accept(this);
            row[column.Name] = value;
        }

        return row;
    }

    /// <summary>
    /// Processes nested SELECT groups using Cartesian product semantics.
    /// Each nested select creates a Cartesian product with current rows.
    /// Used for ViewDefinition "select" property.
    /// </summary>
    private IEnumerable<Dictionary<string, object?>> ProcessNestedSelectsCartesian(
        IEnumerable<Dictionary<string, object?>> currentRows,
        ImmutableArray<SelectExpression> nestedSelects)
    {
        if (nestedSelects.IsEmpty)
        {
            return currentRows;
        }

        var rows = currentRows.ToList();

        // Each nested select creates Cartesian product with existing rows
        for (int i = 0; i < nestedSelects.Length; i++)
        {
            var nestedSelect = nestedSelects[i];
            var newRows = new List<Dictionary<string, object?>>();

            foreach (var currentRow in rows)
            {
                var nestedRows = ((IEnumerable<Dictionary<string, object?>>)nestedSelect.Accept(this)!).ToList();

                if (nestedRows.Count == 0)
                {
                    // Cartesian product semantics: Only drop the row if nested select has forEach/forEachOrNull
                    // Regular selects without forEach should preserve rows with null values for their columns
                    if (nestedSelect.ForEach == null && nestedSelect.ForEachOrNull == null)
                    {
                        // No forEach/forEachOrNull: keep the row (columns will be null)
                        newRows.Add(currentRow);
                    }
                    // If it has forEach/forEachOrNull: drop the row (Cartesian product with empty set)
                }
                else
                {
                    // Normal Cartesian product: merge each nested row with current row
                    foreach (var nestedRow in nestedRows)
                    {
                        var mergedRow = new Dictionary<string, object?>(currentRow);
                        foreach (var kvp in nestedRow)
                        {
                            mergedRow[kvp.Key] = kvp.Value;
                        }
                        newRows.Add(mergedRow);
                    }
                }
            }

            rows = newRows;
        }

        return rows;
    }

    /// <summary>
    /// Processes UnionAll groups using concatenation semantics.
    /// Each unionAll result is concatenated (not Cartesian product).
    /// Used for ViewDefinition "unionAll" property.
    /// </summary>
    private IEnumerable<Dictionary<string, object?>> ProcessUnionAllConcat(
        IEnumerable<Dictionary<string, object?>> currentRows,
        ImmutableArray<SelectExpression> unionAllGroups)
    {
        if (unionAllGroups.IsEmpty)
        {
            return currentRows;
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var currentRow in currentRows)
        {
            // Evaluate all unionAll groups and concatenate their results
            foreach (var unionAllGroup in unionAllGroups)
            {
                var unionRows = ((IEnumerable<Dictionary<string, object?>>)unionAllGroup.Accept(this)!).ToList();

                // Concatenate: merge each union row with current row
                foreach (var unionRow in unionRows)
                {
                    var mergedRow = new Dictionary<string, object?>(currentRow);
                    foreach (var kvp in unionRow)
                    {
                        mergedRow[kvp.Key] = kvp.Value;
                    }
                    result.Add(mergedRow);
                }
            }
        }

        return result;
    }


    /// <summary>
    /// Creates a FHIRPath evaluation context with constants from the ViewDefinition.
    /// Wraps primitive constant values as ITypedElement for FHIRPath compatibility.
    /// </summary>
    private static EvaluationContext CreateEvaluationContext(ViewDefinitionExpression viewDef)
    {
        var context = new EvaluationContext();

        foreach (var constant in viewDef.Constants)
        {
            if (constant.Value != null)
            {
                // Wrap primitive values as ITypedElement
                var typedElement = WrapConstantValue(constant.Value);
                context.SetEnvironmentVariable(constant.Name, typedElement);
            }
        }

        return context;
    }

    /// <summary>
    /// Wraps a primitive constant value as an IElement for FHIRPath context.
    /// </summary>
    private static IElement WrapConstantValue(object value)
    {
        // Simple wrapper that stores the value for use in FHIRPath evaluation
        return new PrimitiveValueElement(value);
    }

    /// <summary>
    /// Simple wrapper for primitive constant values to be used as IElement.
    /// </summary>
    private class PrimitiveValueElement : IElement
    {
        private readonly object _value;
        private readonly string _type;

        public PrimitiveValueElement(object value)
        {
            _value = value;
            // Use centralized type name converter
            _type = FhirPathEvaluator.GetFhirPathTypeName(value);
        }

        public string Name => "value";
        public object? Value => _value;
        public string InstanceType => _type;
        public string Location => "";
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => null;
    }


    /// <summary>
    /// Extracts the primitive value from a FHIRPath result.
    /// </summary>
    private static object? ExtractValue(object? fhirPathResult)
    {
        if (fhirPathResult == null)
        {
            return null;
        }

        // If it's already a primitive type, return it
        if (fhirPathResult is string or int or long or decimal or bool or DateTime)
        {
            return fhirPathResult;
        }

        // If it's an IElement, extract the primitive value
        if (fhirPathResult is IElement element)
        {
            return element.Value;
        }

        // Fallback: convert to string
        return fhirPathResult.ToString();
    }

    /// <summary>
    /// Checks if a FHIRPath result evaluates to true (for WHERE clauses).
    /// </summary>
    private static bool IsTrue(IEnumerable<IElement> results)
    {
        // Use explicit enumerator to avoid LINQ casting issues
        using var enumerator = results.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        var firstResult = enumerator.Current;

        // Boolean true
        if (firstResult.Value is bool b)
        {
            return b;
        }

        // Non-empty collection is truthy (we already know we have at least one element)
        return true;
    }

    /// <summary>
    /// Converts a value to the specified SQL type (string, integer, boolean, etc.).
    /// </summary>
    private static object? ConvertToSqlType(object? value, string? targetType)
    {
        if (value == null)
        {
            return null;
        }

        // No type specified - return as-is
        if (string.IsNullOrEmpty(targetType))
        {
            return value;
        }

        try
        {
            // Use uppercase for consistency with CA1308, but match against lowercase input
            return targetType.ToUpperInvariant() switch
            {
                "STRING" => value.ToString(),
                "INTEGER" => Convert.ToInt32(value),
                "DECIMAL" => Convert.ToDecimal(value),
                "BOOLEAN" => value switch
                {
                    bool b => b,
                    string s => bool.Parse(s),
                    _ => Convert.ToBoolean(value)
                },
                "DATE" => value switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-dd"),
                    DateTimeOffset dto => dto.ToString("yyyy-MM-dd"),
                    string s => s, // Already formatted
                    _ => value.ToString()
                },
                "DATETIME" => value switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    string s => s, // Already formatted
                    _ => value.ToString()
                },
                _ => value // Unknown type - return as-is
            };
        }
        catch
        {
            // Conversion failed - return original value
            return value;
        }
    }

    /// <summary>
    /// Formats an array of values as a JSON array string.
    /// </summary>
    private static string FormatArrayAsJson(object?[] values)
    {
        if (values.Length == 0)
        {
            return "[]";
        }

        var formattedValues = values.Select(v =>
        {
            if (v == null)
            {
                return "null";
            }

            return v switch
            {
                string s => $"\"{EscapeJsonString(s)}\"",
                bool b => b ? "true" : "false",
                int or long or decimal or double or float => v.ToString(),
                _ => $"\"{EscapeJsonString(v.ToString()!)}\""
            };
        });

        return $"[{string.Join(", ", formattedValues)}]";
    }

    /// <summary>
    /// Escapes a string for use in JSON.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    /// <summary>
    /// Recursively collects all items reachable via the repeat paths.
    /// Implements breadth-first traversal to collect items at all nesting depths.
    /// Per SQL on FHIR spec: evaluates repeat paths starting from the root, collecting
    /// ALL items found at any depth (including the initial matches from root).
    /// </summary>
    /// <param name="root">The root element to start traversal from</param>
    /// <param name="repeatPaths">Array of FHIRPath expressions defining recursive paths</param>
    /// <returns>Flat list of all items found at any depth via the repeat paths</returns>
    private List<IElement> RecursivelyCollectItems(
        IElement root,
        ImmutableArray<FhirPath.Expressions.Expression> repeatPaths)
    {
        var result = new List<IElement>();

        // Helper function for depth-first recursive traversal
        void DepthFirstTraversal(IElement element, int depth)
        {
            // Add current element to results
            result.Add(element);

            // Recursively follow all repeat paths from this element (depth-first)
            foreach (var repeatPath in repeatPaths)
            {
                var children = _evaluator.Evaluate(element, repeatPath, _currentContext!);

                // Recursively traverse each child
                foreach (var child in children)
                {
                    DepthFirstTraversal(child, depth + 1);
                }
            }
        }

        // Start by evaluating repeat paths from the root (not the root itself)
        foreach (var repeatPath in repeatPaths)
        {
            var initialItems = _evaluator.Evaluate(root, repeatPath, _currentContext!);

            // Traverse each initial item depth-first
            foreach (var item in initialItems)
            {
                DepthFirstTraversal(item, 0);
            }
        }

        return result;
    }
}
