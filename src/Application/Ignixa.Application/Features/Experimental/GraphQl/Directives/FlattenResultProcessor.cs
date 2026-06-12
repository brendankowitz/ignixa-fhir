// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Language;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

/// <summary>
/// Post-processes GraphQL result data to apply @flatten and @slice directives.
/// Walks the query document and result data in parallel, restructuring data
/// where directives are found.
/// </summary>
internal static class FlattenResultProcessor
{
    /// <summary>
    /// Processes the result data, applying @flatten and @slice transformations to the
    /// operation matching <paramref name="operationName"/> (or the first when null).
    /// </summary>
    internal static void Process(DocumentNode document, string? operationName, IDictionary<string, object?> data)
    {
        var operation = SelectOperation(document, operationName);
        if (operation?.SelectionSet is null)
            return;

        ProcessSelectionSet(operation.SelectionSet, data);
    }

    private static OperationDefinitionNode? SelectOperation(DocumentNode document, string? operationName)
    {
        var operations = document.Definitions.OfType<OperationDefinitionNode>();
        return operationName is not null
            ? operations.FirstOrDefault(op => op.Name?.Value == operationName)
            : operations.FirstOrDefault();
    }

    /// <summary>
    /// Deep-copies an <see cref="IReadOnlyDictionary{TKey,TValue}"/> tree into mutable
    /// <see cref="Dictionary{TKey,TValue}"/> and <see cref="List{T}"/> instances so the
    /// processor can restructure the data in place.
    /// </summary>
    internal static Dictionary<string, object?> DeepCopyData(IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count);
        foreach (var (key, value) in source)
            result[key] = DeepCopyValue(value);
        return result;
    }

    private static object? DeepCopyValue(object? value) => value switch
    {
        IReadOnlyDictionary<string, object?> dict => DeepCopyData(dict),
        IReadOnlyList<object?> list => list.Select(DeepCopyValue).ToList(),
        _ => value,
    };

    private static void ProcessSelectionSet(
        SelectionSetNode selectionSet,
        IDictionary<string, object?> parentData)
    {
        // Collect directives before modifying (can't modify dict while iterating)
        var fieldsToFlatten = new List<(string fieldName, FieldNode fieldNode)>();
        var fieldsToSlice = new List<(string fieldName, FieldNode fieldNode, string path)>();
        var fieldsToFirst = new List<string>();
        var fieldsToSingleton = new List<string>();

        foreach (var selection in selectionSet.Selections.OfType<FieldNode>())
        {
            var fieldName = selection.Alias?.Value ?? selection.Name.Value;

            if (HasDirective(selection, "flatten"))
                fieldsToFlatten.Add((fieldName, selection));

            if (HasDirective(selection, "first"))
                fieldsToFirst.Add(fieldName);

            if (HasDirective(selection, "singleton"))
                fieldsToSingleton.Add(fieldName);

            var slicePath = GetDirectiveArgument(selection, "slice", "path");
            if (slicePath is not null)
                fieldsToSlice.Add((fieldName, selection, slicePath));

            // Recurse into nested selections (before flattening modifies the tree)
            if (selection.SelectionSet is not null && parentData.TryGetValue(fieldName, out var childValue))
            {
                switch (childValue)
                {
                    case IDictionary<string, object?> childDict:
                        ProcessSelectionSet(selection.SelectionSet, childDict);
                        break;
                    case IList<object?> childList:
                        foreach (var item in childList)
                        {
                            if (item is IDictionary<string, object?> itemDict)
                                ProcessSelectionSet(selection.SelectionSet, itemDict);
                        }

                        break;
                }
            }
        }

        // @slice must run before @flatten when both are on the same field,
        // since slice removes the field and promotes suffixed children to the parent.
        foreach (var (fieldName, _, path) in fieldsToSlice)
        {
            if (parentData.TryGetValue(fieldName, out var fieldValue))
                ApplySlice(parentData, fieldName, fieldValue, path);
        }

        foreach (var (fieldName, _) in fieldsToFlatten)
        {
            if (parentData.TryGetValue(fieldName, out var fieldValue))
                ApplyFlatten(parentData, fieldName, fieldValue);
        }

        // @first: replace list with its first element (or null if empty)
        foreach (var fieldName in fieldsToFirst)
        {
            if (parentData.TryGetValue(fieldName, out var fieldValue) && fieldValue is IList<object?> list)
                parentData[fieldName] = list.Count > 0 ? list[0] : null;
        }

        // @singleton: assert list contains exactly one element
        foreach (var fieldName in fieldsToSingleton)
        {
            if (parentData.TryGetValue(fieldName, out var fieldValue) && fieldValue is IList<object?> list)
            {
                parentData[fieldName] = list.Count switch
                {
                    1 => list[0],
                    0 => null,
                    _ => throw new SingletonDirectiveViolationException(
                        $"@singleton assertion failed: field '{fieldName}' contains {list.Count} elements, expected exactly 1."),
                };
            }
        }
    }

    private static void ApplyFlatten(
        IDictionary<string, object?> parentData,
        string fieldName,
        object? fieldValue)
    {
        parentData.Remove(fieldName);

        switch (fieldValue)
        {
            // Single object: promote all its properties to parent
            case IDictionary<string, object?> dict:
                foreach (var (key, value) in dict)
                    parentData[key] = value;
                break;

            // Array of objects: collate each property across all items into lists
            case IList<object?> list:
            {
                var collated = new Dictionary<string, List<object?>>();
                foreach (var item in list)
                {
                    if (item is not IDictionary<string, object?> itemDict)
                        continue;

                    foreach (var (key, value) in itemDict)
                    {
                        if (!collated.TryGetValue(key, out var bucket))
                        {
                            bucket = [];
                            collated[key] = bucket;
                        }

                        if (value is IList<object?> nestedList)
                            bucket.AddRange(nestedList);
                        else
                            bucket.Add(value);
                    }
                }

                foreach (var (key, bucket) in collated)
                    parentData[key] = bucket;
                break;
            }
        }
    }

    private static void ApplySlice(
        IDictionary<string, object?> parentData,
        string fieldName,
        object? fieldValue,
        string path)
    {
        if (fieldValue is not IList<object?> list)
            return;

        parentData.Remove(fieldName);

        var headSegment = HeadSegment(path);

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not IDictionary<string, object?> itemDict)
                continue;

            var suffix = path == "$index"
                ? i.ToString()
                : ResolvePath(itemDict, path)?.ToString() ?? i.ToString();

            foreach (var (key, value) in itemDict)
            {
                if (key == headSegment)
                    continue;
                AssignDisambiguated(parentData, $"{key}.{suffix}", value);
            }
        }
    }

    private static string? HeadSegment(string path)
    {
        if (path == "$index")
            return null;

        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 ? path[..dot] : path;
    }

    /// <summary>
    /// Resolves a dotted path (e.g. <c>name.family</c>) against a result item by walking
    /// nested dictionaries, taking the first element of any intermediate list to match
    /// FHIRPath single-value semantics. Returns null when any segment is missing.
    /// </summary>
    private static object? ResolvePath(object? item, string path)
    {
        var current = item;
        foreach (var segment in path.Split('.'))
        {
            if (current is IList<object?> { Count: > 0 } list)
                current = list[0];

            if (current is not IDictionary<string, object?> dict
                || !dict.TryGetValue(segment, out current))
            {
                return null;
            }
        }

        if (current is IList<object?> { Count: > 0 } tail)
            current = tail[0];

        return current;
    }

    private static void AssignDisambiguated(
        IDictionary<string, object?> parentData, string key, object? value)
    {
        if (!parentData.ContainsKey(key))
        {
            parentData[key] = value;
            return;
        }

        for (var index = 1; ; index++)
        {
            var candidate = $"{key}.{index}";
            if (!parentData.ContainsKey(candidate))
            {
                parentData[candidate] = value;
                return;
            }
        }
    }

    private static bool HasDirective(FieldNode field, string directiveName) =>
        field.Directives.Any(d => d.Name.Value == directiveName);

    private static string? GetDirectiveArgument(FieldNode field, string directiveName, string argName)
    {
        var directive = field.Directives.FirstOrDefault(d => d.Name.Value == directiveName);
        var arg = directive?.Arguments.FirstOrDefault(a => a.Name.Value == argName);
        return arg?.Value is StringValueNode strValue ? strValue.Value : null;
    }
}
