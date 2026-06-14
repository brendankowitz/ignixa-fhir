/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Stateless evaluator for SQL on FHIR ViewDefinition expressions.
 * Context (resource + environment variables) is threaded as explicit parameters —
 * no mutable fields, safe to reuse across calls.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Expressions;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNode migration pending

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Evaluates ViewDefinition expressions against FHIR resources.
/// Stateless: EvaluationContext is threaded as a method parameter, never stored as a field.
/// </summary>
internal class SqlOnFhirEvaluationVisitor
{
    private readonly FhirPathEvaluator _fhirPath;

    public SqlOnFhirEvaluationVisitor()
    {
        _fhirPath = new FhirPathEvaluator();
    }

    /// <summary>
    /// Evaluates a ViewDefinition expression against a FHIR resource.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> Evaluate(
        ViewDefinitionExpression viewDef,
        IElement resource,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var context = CreateEvaluationContext(viewDef, resource, variables);
        return EvaluateViewDefinition(viewDef, resource, context);
    }

    /// <summary>
    /// Evaluates a ViewDefinition expression against multiple resources with correct UNION ALL ordering.
    /// When a top-level select has unionAll without forEach, each branch is evaluated across all
    /// resources before moving to the next branch (SQL UNION ALL semantics).
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> EvaluateBatch(
        ViewDefinitionExpression viewDef,
        IEnumerable<IElement> resources,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var resourceList = resources.ToList();
        if (resourceList.Count == 0)
            return [];

        var unionAllIndex = IndexOfBatchOrderedUnionAllSelect(viewDef);
        if (unionAllIndex < 0)
            return resourceList.SelectMany(r => Evaluate(viewDef, r, variables)).ToList();

        return EvaluateBatchWithUnionAllOrdering(viewDef, unionAllIndex, resourceList, variables);
    }

    private static int IndexOfBatchOrderedUnionAllSelect(ViewDefinitionExpression viewDef)
    {
        for (int i = 0; i < viewDef.Select.Length; i++)
            if (IsBatchOrderedUnionAllSelect(viewDef.Select[i]))
                return i;
        return -1;
    }

    private static bool IsBatchOrderedUnionAllSelect(SelectExpression s)
        => !s.UnionAll.IsEmpty
           && s.ForEach == null
           && s.ForEachOrNull == null
           && s.Repeat.IsEmpty
           && s.Columns.IsEmpty
           && s.NestedSelect.IsEmpty
           && s.UnionAll.All(branch =>
                branch.ForEach == null
                && branch.ForEachOrNull == null
                && branch.Repeat.IsEmpty);

    private IEnumerable<Dictionary<string, object?>> EvaluateBatchWithUnionAllOrdering(
        ViewDefinitionExpression viewDef,
        int selectIndex,
        List<IElement> resources,
        IReadOnlyDictionary<string, string>? variables)
    {
        var unionAllSelect = viewDef.Select[selectIndex];

        var result = new List<Dictionary<string, object?>>();

        // SQL UNION ALL semantics: evaluate every resource through one branch before the next.
        // Each branch is evaluated as a standalone single-branch view so WHERE clauses, sibling
        // selects, and nested selects keep their normal per-resource behaviour — only the branch
        // dimension is reordered to the outer loop.
        foreach (var branch in unionAllSelect.UnionAll)
        {
            var singleBranchSelect = unionAllSelect with { UnionAll = [branch] };
            var branchView = viewDef with { Select = viewDef.Select.SetItem(selectIndex, singleBranchSelect) };

            foreach (var resource in resources)
            {
                var context = CreateEvaluationContext(branchView, resource, variables);
                result.AddRange(EvaluateViewDefinition(branchView, resource, context));
            }
        }

        return result;
    }

    private static EvaluationContext CreateEvaluationContext(
        ViewDefinitionExpression viewDef,
        IElement resource,
        IReadOnlyDictionary<string, string>? variables)
    {
        var context = new EvaluationContext() with { RootResource = resource };
        foreach (var constant in viewDef.Constants)
            if (constant.Value != null)
                context = context.WithEnvironmentVariable(constant.Name, new PrimitiveValueElement(constant.Value));
        // Caller-supplied variables override ViewDefinition constants if names collide (caller wins).
        if (variables != null)
            foreach (var (name, value) in variables)
                context = context.WithEnvironmentVariable(name, new PrimitiveValueElement(value));
        // rowIndex injected last so it cannot be shadowed by user-defined constants or variables
        context = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(0));
        return context;
    }

    private IEnumerable<Dictionary<string, object?>> EvaluateViewDefinition(
        ViewDefinitionExpression node, IElement resource, EvaluationContext context)
    {
        foreach (var where in node.Where)
            if (!EvaluateWhere(where, resource, context))
                return [];

        if (node.Select.IsEmpty)
            return [];

        var rows = EvaluateSelect(node.Select[0], resource, context).ToList();

        for (int i = 1; i < node.Select.Length; i++)
            rows = MergeSelectGroup(rows, node.Select[i], resource, context);

        return rows;
    }

    private List<Dictionary<string, object?>> MergeSelectGroup(
        List<Dictionary<string, object?>> currentRows,
        SelectExpression select,
        IElement resource,
        EvaluationContext context)
    {
        if (select.ForEach == null && select.ForEachOrNull == null && select.Repeat.IsEmpty && select.UnionAll.IsEmpty)
        {
            foreach (var row in currentRows)
            {
                var cols = EvaluateColumns(select.Columns, resource, context);
                foreach (var kvp in cols) row[kvp.Key] = kvp.Value;
            }
            return currentRows;
        }

        var selectRows = EvaluateSelect(select, resource, context);

        if (selectRows.Count == 0)
        {
            if (select.ForEachOrNull != null)
            {
                foreach (var row in currentRows)
                    foreach (var colName in GetAllColumnNames(select))
                        row[colName] = null;
                return currentRows;
            }
            return [];
        }

        var newRows = new List<Dictionary<string, object?>>();
        foreach (var selectRow in selectRows)
            foreach (var currentRow in currentRows)
            {
                var merged = new Dictionary<string, object?>(currentRow);
                foreach (var kvp in selectRow) merged[kvp.Key] = kvp.Value;
                newRows.Add(merged);
            }
        return newRows;
    }

    private List<Dictionary<string, object?>> EvaluateSelect(
        SelectExpression node, IElement resource, EvaluationContext context)
    {
        if (node.ForEach == null && node.ForEachOrNull == null && node.Repeat.IsEmpty)
        {
            var row = EvaluateColumns(node.Columns, resource, context);
            var rows = ProcessNestedSelects([row], node.NestedSelect, resource, context);
            return ProcessUnionAll(rows, node.UnionAll, resource, context);
        }

        if (!node.Repeat.IsEmpty)
        {
            var allItems = RecursivelyCollectItems(resource, node.Repeat, context);
            var repeatRows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < allItems.Count; i++)
            {
                var iterContext = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(i));
                var row = EvaluateColumns(node.Columns, allItems[i], iterContext);
                var rowsForItem = ProcessNestedSelects([row], node.NestedSelect, allItems[i], iterContext);
                rowsForItem = ProcessUnionAll(rowsForItem, node.UnionAll, allItems[i], iterContext);
                repeatRows.AddRange(rowsForItem);
            }
            return repeatRows;
        }

        var forEachExpr = node.ForEach ?? node.ForEachOrNull!;
        var items = _fhirPath.Evaluate(resource, forEachExpr, context).ToList();
        var forEachRows = new List<Dictionary<string, object?>>();

        for (int i = 0; i < items.Count; i++)
        {
            var iterContext = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(i));
            var row = EvaluateColumns(node.Columns, items[i], iterContext);
            var rowsForItem = ProcessNestedSelects([row], node.NestedSelect, items[i], iterContext);
            rowsForItem = ProcessUnionAll(rowsForItem, node.UnionAll, items[i], iterContext);
            forEachRows.AddRange(rowsForItem);
        }

        // forEachOrNull: if collection was empty, emit one null row with rowIndex=0.
        // Evaluate columns against the base resource with rowIndex=0 context so that
        // context-only expressions like %rowIndex return 0, while resource-path expressions
        // that expect a forEach element return null (path won't resolve against the resource).
        if (node.ForEachOrNull != null && forEachRows.Count == 0)
        {
            var nullContext = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(0));
            var nullRow = EvaluateNullRowColumns(node.Columns, resource, nullContext);
            foreach (var colName in GetAllColumnNames(node).Skip(node.Columns.Length))
                nullRow.TryAdd(colName, null);
            var nullRowProcessed = ProcessNestedSelects([nullRow], node.NestedSelect, resource, nullContext);
            forEachRows.AddRange(nullRowProcessed);
        }

        return forEachRows;
    }

    private Dictionary<string, object?> EvaluateColumns(
        ImmutableArray<ColumnExpression> columns, IElement resource, EvaluationContext context)
    {
        var row = new Dictionary<string, object?>();
        foreach (var col in columns)
            row[col.Name] = EvaluateColumn(col, resource, context);
        return row;
    }

    private Dictionary<string, object?> EvaluateNullRowColumns(
        ImmutableArray<ColumnExpression> columns, IElement resource, EvaluationContext context)
    {
        var row = new Dictionary<string, object?>();
        foreach (var col in columns)
        {
            List<IElement> results;
            try
            {
                results = _fhirPath.Evaluate(resource, col.Path, context).ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // Path evaluation against the base resource is expected to fail for expressions
                // that navigate into the forEach element (e.g. "family" when the element is absent).
                // Context-only expressions like %rowIndex succeed and return their value normally.
                row[col.Name] = null;
                continue;
            }

            if (col.Collection)
            {
                var values = results.Select(ExtractValue).Select(v => ConvertToSqlType(v, col.Type)).ToArray();
                row[col.Name] = FormatArrayAsJson(values);
            }
            else
            {
                row[col.Name] = ConvertToSqlType(ExtractValue(results.FirstOrDefault()), col.Type);
            }
        }
        return row;
    }

    private object? EvaluateColumn(ColumnExpression node, IElement resource, EvaluationContext context)
    {
        var results = _fhirPath.Evaluate(resource, node.Path, context).ToList();

        if (node.Collection)
        {
            var values = results.Select(ExtractValue).Select(v => ConvertToSqlType(v, node.Type)).ToArray();
            return FormatArrayAsJson(values);
        }

        if (results.Count > 1)
            throw new InvalidOperationException(
                $"Column '{node.Name}' has collection=false but FHIRPath expression returned {results.Count} values. " +
                "Either set collection=true or ensure the path returns at most one value.");

        return ConvertToSqlType(ExtractValue(results.FirstOrDefault()), node.Type);
    }

    private bool EvaluateWhere(WhereExpression node, IElement resource, EvaluationContext context)
    {
        var result = _fhirPath.Evaluate(resource, node.Filter, context);
        return IsTrue(result);
    }

    private List<Dictionary<string, object?>> ProcessNestedSelects(
        IEnumerable<Dictionary<string, object?>> current,
        ImmutableArray<SelectExpression> nestedSelects,
        IElement resource,
        EvaluationContext context)
    {
        if (nestedSelects.IsEmpty)
            return current.ToList();

        var rows = current.ToList();
        foreach (var nested in nestedSelects)
        {
            var newRows = new List<Dictionary<string, object?>>();
            foreach (var currentRow in rows)
            {
                var nestedRows = EvaluateSelect(nested, resource, context);
                if (nestedRows.Count == 0)
                {
                    if (nested.ForEach == null && nested.ForEachOrNull == null)
                        newRows.Add(currentRow);
                    // else: Cartesian product with empty = drop row
                }
                else
                {
                    foreach (var nestedRow in nestedRows)
                    {
                        var merged = new Dictionary<string, object?>(currentRow);
                        foreach (var kvp in nestedRow) merged[kvp.Key] = kvp.Value;
                        newRows.Add(merged);
                    }
                }
            }
            rows = newRows;
        }
        return rows;
    }

    private List<Dictionary<string, object?>> ProcessUnionAll(
        IEnumerable<Dictionary<string, object?>> current,
        ImmutableArray<SelectExpression> unionAll,
        IElement resource,
        EvaluationContext context)
    {
        if (unionAll.IsEmpty)
            return current.ToList();

        var result = new List<Dictionary<string, object?>>();
        foreach (var currentRow in current)
            foreach (var branch in unionAll)
            {
                var branchRows = EvaluateSelect(branch, resource, context);
                foreach (var branchRow in branchRows)
                {
                    var merged = new Dictionary<string, object?>(currentRow);
                    foreach (var kvp in branchRow) merged[kvp.Key] = kvp.Value;
                    result.Add(merged);
                }
            }
        return result;
    }

    private List<IElement> RecursivelyCollectItems(
        IElement root,
        ImmutableArray<FhirPath.Expressions.Expression> repeatPaths,
        EvaluationContext context)
    {
        var result = new List<IElement>();

        void Traverse(IElement element)
        {
            result.Add(element);
            foreach (var path in repeatPaths)
                foreach (var child in _fhirPath.Evaluate(element, path, context))
                    Traverse(child);
        }

        foreach (var path in repeatPaths)
            foreach (var item in _fhirPath.Evaluate(root, path, context))
                Traverse(item);

        return result;
    }

    private static IEnumerable<string> GetAllColumnNames(SelectExpression select)
    {
        foreach (var col in select.Columns)
            yield return col.Name;
        foreach (var nested in select.NestedSelect)
            foreach (var name in GetAllColumnNames(nested))
                yield return name;
        foreach (var union in select.UnionAll)
            foreach (var name in GetAllColumnNames(union))
                yield return name;
    }

    private class PrimitiveValueElement : IElement
    {
        private readonly object _value;
        private readonly string _type;

        public PrimitiveValueElement(object value)
        {
            _value = value;
            _type = FhirPathEvaluator.GetFhirPathTypeName(value);
        }

        public string Name => "value";
        public object? Value => _value;
        public string InstanceType => _type;
        public string Location => "";
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => null;
    }

    private static object? ExtractValue(object? fhirPathResult)
    {
        if (fhirPathResult == null)
            return null;

        if (fhirPathResult is string or int or long or decimal or bool or DateTime)
            return fhirPathResult;

        if (fhirPathResult is IElement element)
            return element.Value;

        return fhirPathResult.ToString();
    }

    private static bool IsTrue(IEnumerable<IElement> results)
    {
        using var enumerator = results.GetEnumerator();
        if (!enumerator.MoveNext())
            return false;

        var firstResult = enumerator.Current;

        if (firstResult.Value is bool b)
            return b;

        return true;
    }

    private static object? ConvertToSqlType(object? value, string? targetType)
    {
        if (value == null)
            return null;

        if (string.IsNullOrEmpty(targetType))
            return value;

        try
        {
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
                    string s => s,
                    _ => value.ToString()
                },
                "DATETIME" => value switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    string s => s,
                    _ => value.ToString()
                },
                _ => value
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot convert value '{value}' ({value.GetType().Name}) to SQL type '{targetType}'", ex);
        }
    }

    private static string FormatArrayAsJson(object?[] values)
    {
        if (values.Length == 0)
            return "[]";

        var formattedValues = values.Select(v =>
        {
            if (v == null)
                return "null";

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

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}
