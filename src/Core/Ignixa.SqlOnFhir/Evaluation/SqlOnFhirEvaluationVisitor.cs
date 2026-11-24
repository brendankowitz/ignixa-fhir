/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Stateless evaluator for SQL on FHIR ViewDefinition expressions.
 * Context (resource + environment variables) is threaded as explicit parameters —
 * no mutable fields, safe to reuse across calls.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Expressions;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNode migration pending

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Evaluates ViewDefinition expressions against FHIR resources.
/// Stateless: EvaluationContext is threaded as a method parameter, never stored as a field.
/// </summary>
internal class SqlOnFhirEvaluationVisitor
{
    private static readonly JsonSerializerOptions ComplexItemJsonOptions = new()
    {
        Encoder = MinimalJsonEncoder.Instance,
        // JsonNode.ToJsonString fails on a JsonValue wrapping a CLR object unless a resolver is set.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

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
                context = context.WithEnvironmentVariable(
                    constant.Name, new PrimitiveValueElement(constant.Value, constant.ValueType));
        // Caller-supplied variables override ViewDefinition constants if names collide (caller wins).
        //
        // The caller wins on the *value*; the declared type belongs to the slot, so a variable that
        // overrides a constant inherits that constant's type. Without this, overriding a valueDate
        // constant would silently retype it as System.String and reintroduce the comparison failure the
        // constant path was fixed for - and since variables arrive as IReadOnlyDictionary<string,
        // string>, the caller has no way to say otherwise. Declaring the constant with the right
        // value[x] and overriding its value at runtime is therefore the supported way to pass a typed
        // variable.
        //
        // SCOPE, measured rather than assumed. ValidateConstantReferences does not require every %name to
        // be a declared constant: it exempts context, resource, rootResource, ucum, sct, loinc, rowIndex,
        // and the vs-/ext- families in their delimited spelling. Of those, ucum/sct/loinc and every
        // %`vs-*`/%`ext-*` name reach this loop untyped, binding the caller's string with no constant to
        // inherit from - and System.String is correct for them, not a defect: FHIRPath defines all of them
        // as fixed URIs. `birthDate < %ucum` throwing "Cannot compare 'date' with 'string'" is the correct
        // answer to putting a date in a URI slot, not a typing failure. The engine's override of those
        // names is a deliberate, documented feature (EvaluationContext's remarks) and is left working.
        //
        // context/resource/rootResource cannot reach here untyped - TryGetEnvironmentVariable's switch
        // answers them before it consults Environment - and rowIndex is re-injected below and wins. As of
        // issue #439, SqlOnFhirEvaluator.ValidateVariables rejects a variables entry using any of those
        // four before this method runs, so the paragraph above records why such an override was silently
        // ineffective, not a case this loop still has to handle.
        //
        // The vs-/ext- families reach here only in their delimited spelling. The tokenizer now lexes a
        // bare "%vs-x" as one token rather than %vs / minus / x (issue #438), but that is a lexical change
        // only: HAPI's engine and GetStandardConstant both expand for the backtick spelling alone, so a
        // bare %vs-x is an ordinary undeclared constant name that ValidateConstantReferences rejects up
        // front rather than letting it fail later as an undefined environment variable.
        //
        // A caller wanting a typed value under one of these names declares a constant of that name and
        // overrides its value, as above. Pinned by the GivenAPredefinedVariableName_* tests, by
        // SqlOnFhirEvaluatorTests.GivenAViewDefinitionSelectingAStandardPrefixedConstant (declared in
        // FhirPathColumnEvaluatorTests.cs), and by Ignixa.FhirPath.Tests' EnvironmentVariableResolutionTests.
        if (variables != null)
        {
            // Built with the indexer, not ToDictionary: two constants sharing a name is malformed input
            // that the loop above resolves as last-wins rather than rejecting, and ToDictionary would
            // turn that into a throw from a code path that only exists to supply a type.
            var declaredTypes = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var constant in viewDef.Constants)
                declaredTypes[constant.Name] = constant.ValueType;

            foreach (var (name, value) in variables)
            {
                declaredTypes.TryGetValue(name, out var declaredType);
                context = context.WithEnvironmentVariable(name, new PrimitiveValueElement(value, declaredType));
            }
        }
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
        SelectExpression node, IElement? focus, EvaluationContext context)
    {
        if (node.ForEach == null && node.ForEachOrNull == null && node.Repeat.IsEmpty)
        {
            var row = EvaluateColumns(node.Columns, focus, context);
            var rows = ProcessNestedSelects([row], node.NestedSelect, focus, context);
            return ProcessUnionAll(rows, node.UnionAll, focus, context);
        }

        if (!node.Repeat.IsEmpty)
        {
            var allItems = RecursivelyCollectItems(focus, node.Repeat, context);
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
        var items = EvaluatePath(focus, forEachExpr, context).ToList();
        var forEachRows = new List<Dictionary<string, object?>>();

        for (int i = 0; i < items.Count; i++)
        {
            var iterContext = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(i));
            var row = EvaluateColumns(node.Columns, items[i], iterContext);
            var rowsForItem = ProcessNestedSelects([row], node.NestedSelect, items[i], iterContext);
            rowsForItem = ProcessUnionAll(rowsForItem, node.UnionAll, items[i], iterContext);
            forEachRows.AddRange(rowsForItem);
        }

        // forEachOrNull: if the collection was empty, evaluate the whole select once for the absent item
        // (a null focus) with rowIndex=0, so neither its columns nor its nested selects see the parent.
        if (node.ForEachOrNull != null && items.Count == 0)
        {
            var nullContext = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(0));
            var nullRow = EvaluateColumns(node.Columns, null, nullContext);
            var nullRows = ProcessNestedSelects([nullRow], node.NestedSelect, null, nullContext);
            forEachRows.AddRange(ProcessUnionAll(nullRows, node.UnionAll, null, nullContext));
        }

        return forEachRows;
    }

    /// <summary>
    /// Evaluates <paramref name="path"/> against <paramref name="focus"/>. A <see langword="null"/> focus is the
    /// absent <c>forEachOrNull</c> item: navigation yields nothing, while %rootResource, %rowIndex and constants
    /// still resolve.
    /// </summary>
    private IEnumerable<IElement> EvaluatePath(
        IElement? focus, FhirPath.Expressions.Expression path, EvaluationContext context) =>
        focus is null
            // Fork the variable scope as FhirPathEvaluator.Evaluate does for every call.
            ? path.AcceptVisitor(_fhirPath, context.WithFocus([]).ForkVariableScope())
            : _fhirPath.Evaluate(focus, path, context);

    private Dictionary<string, object?> EvaluateColumns(
        ImmutableArray<ColumnExpression> columns, IElement? focus, EvaluationContext context)
    {
        var row = new Dictionary<string, object?>();
        foreach (var col in columns)
            row[col.Name] = EvaluateColumn(col, focus, context);
        return row;
    }

    private object? EvaluateColumn(ColumnExpression node, IElement? focus, EvaluationContext context)
    {
        var results = EvaluatePath(focus, node.Path, context).ToList();

        if (node.Collection)
            return FormatCollection(results, node);

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
        IElement? focus,
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
                var nestedRows = EvaluateSelect(nested, focus, context);
                // A nested select participates in a cross-join (row_product): an empty result
                // zeroes the product and drops the row. Column-only selects never return empty;
                // this fires when a nested forEach/repeat, including one under a forEachOrNull, collapses.
                foreach (var nestedRow in nestedRows)
                {
                    var merged = new Dictionary<string, object?>(currentRow);
                    foreach (var kvp in nestedRow) merged[kvp.Key] = kvp.Value;
                    newRows.Add(merged);
                }
            }
            rows = newRows;
        }
        return rows;
    }

    private List<Dictionary<string, object?>> ProcessUnionAll(
        IEnumerable<Dictionary<string, object?>> current,
        ImmutableArray<SelectExpression> unionAll,
        IElement? focus,
        EvaluationContext context)
    {
        if (unionAll.IsEmpty)
            return current.ToList();

        var result = new List<Dictionary<string, object?>>();
        foreach (var currentRow in current)
            foreach (var branch in unionAll)
            {
                var branchRows = EvaluateSelect(branch, focus, context);
                foreach (var branchRow in branchRows)
                {
                    var merged = new Dictionary<string, object?>(currentRow);
                    foreach (var kvp in branchRow) merged[kvp.Key] = kvp.Value;
                    result.Add(merged);
                }
            }
        return result;
    }

    /// <summary>
    /// The deepest a <c>repeat</c> may follow its paths. A path that leads back to the item it started from (<c>$this</c>,
    /// <c>$this.where(true)</c>, <c>%resource</c>) would otherwise recurse until the stack overflows, which ends the process and
    /// cannot be caught. Real recursive structures (nested Questionnaire items, Bundle entries) are a handful of levels deep.
    /// </summary>
    internal const int MaxRepeatDepth = 100;

    private List<IElement> RecursivelyCollectItems(
        IElement? root,
        ImmutableArray<FhirPath.Expressions.Expression> repeatPaths,
        EvaluationContext context)
    {
        var result = new List<IElement>();

        void Traverse(IElement element, int depth)
        {
            if (depth > MaxRepeatDepth)
                throw new InvalidOperationException(
                    $"repeat went deeper than {MaxRepeatDepth} levels; a repeat path must lead to child elements");

            // Depth alone does not bound the work: ["item", "item"] over a 30-deep tree visits ~2^30 nodes. Same cap as FhirPath repeat().
            if (result.Count >= RepeatGuardLimits.MaxIterations)
                throw new InvalidOperationException(
                    $"repeat produced more than {RepeatGuardLimits.MaxIterations} nodes; a repeat path must lead to child elements");

            result.Add(element);
            foreach (var path in repeatPaths)
                foreach (var child in _fhirPath.Evaluate(element, path, context))
                    Traverse(child, depth + 1);
        }

        foreach (var path in repeatPaths)
            foreach (var item in EvaluatePath(root, path, context))
                Traverse(item, 1);

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

    /// <summary>
    /// Wraps a value produced by FHIRPath evaluation for consumption by the view runner.
    /// </summary>
    /// <remarks>Declares <see cref="ISystemValueElement"/>; see that interface for why.</remarks>
    private class PrimitiveValueElement : ISystemValueElement
    {
        private readonly object _value;
        private readonly string _type;

        /// <param name="value">The value to wrap.</param>
        /// <param name="declaredType">
        /// The FHIRPath type the producer knows the value to have, or <see langword="null"/> to infer it
        /// from the CLR type.
        /// </param>
        /// <remarks>
        /// Inference is not enough for a ViewDefinition constant. <c>valueDate</c>, <c>valueDateTime</c>,
        /// <c>valueInstant</c> and <c>valueTime</c> all reach here as a <see cref="string"/>, which infers
        /// as System.String, so <c>%cutoff</c> in <c>birthDate &lt; %cutoff</c> compared a date against a
        /// string and threw. The declared type comes off the <c>value[x]</c> suffix, which the parser has
        /// and the value does not.
        /// </remarks>
        public PrimitiveValueElement(object value, string? declaredType = null)
        {
            _value = value;
            _type = declaredType ?? FhirPathEvaluator.GetFhirPathTypeName(value);
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

    private static string FormatCollection(List<IElement> results, ColumnExpression column) =>
        FormatArrayAsJson(results.Select(item => ToCollectionItem(item, column)).ToArray());

    private static object? ToCollectionItem(IElement item, ColumnExpression column)
    {
        // An extension-only primitive has no scalar value; its JSON object is the shadow, not the value.
        // A computed System value such as a Quantity literal has no source JSON and keeps its scalar form.
        if (item.HasPrimitiveValue
            || item is ISystemValueElement
            || FhirPrimitiveExtensions.FromTypeString(item.InstanceType) != FhirPrimitive.None)
            return ConvertToSqlType(item.Value, column.Type);

        if (!string.IsNullOrEmpty(column.Type) && !string.Equals(column.Type, "string", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Column '{column.Name}' has type '{column.Type}' but its collection holds a complex '{item.InstanceType}' value. " +
                "Declare type 'string' or omit the type to write each item as a JSON object.");

        return item.Meta<JsonNode>() as JsonObject
            ?? throw new InvalidOperationException(
                $"Column '{column.Name}' holds a complex '{item.InstanceType}' value at {item.Location} that has no source JSON object to write.");
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
                int or long or decimal or double or float => Convert.ToString(v, CultureInfo.InvariantCulture),
                JsonObject complex => complex.ToJsonString(ComplexItemJsonOptions),
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
