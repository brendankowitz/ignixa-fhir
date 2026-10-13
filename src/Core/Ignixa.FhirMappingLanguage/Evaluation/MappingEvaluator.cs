/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language evaluator.
 * Executes parsed mapping AST to transform FHIR resources.
 */

using System.Collections;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Evaluates FHIR Mapping Language expressions to transform resources.
/// Uses the visitor pattern to traverse the expression tree.
/// </summary>
public class MappingEvaluator
{
    private readonly MappingEvaluatorOptions _options;
    private readonly FhirPathIntegration? _fhirPathIntegration;
    private readonly ImportResolver? _importResolver;
    private readonly IJsonNodeMutator? _mutator;
    private MapExpression? _currentMap;

    // Execution context tracking for enhanced error messages
    private string? _currentGroupName;
    private int? _currentRuleIndex;
    private string? _currentRuleName;

    // Security limit tracking
    private int _recursionDepth;
    private int _elementsCreated;
    private Dictionary<string, IElement> _sharedTargets = new(StringComparer.Ordinal);
    private Dictionary<(object Parent, string Property), IElement> _intermediateTargets = new();
    private HashSet<(object Parent, TargetExpression Target)> _singleTargets = new();
    private readonly HashSet<JsonNode> _sourceRoots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<JsonNode> _targetRoots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IElement, IElement> _sourceSnapshots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MapExpression, Dictionary<string, List<(MapExpression Map, GroupExpression Group)>>> _defaultGroupsBySourceType =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(MapExpression Map, string SourceType, ISchema? SourceSchema, IType TargetDefinition, ISchema? TargetSchema),
        (MapExpression Map, GroupExpression Group)?> _defaultGroupMatches = new();
    private readonly Dictionary<(MapExpression Map, string GroupName), GroupExpression?> _resolvedGroups = new();
    private readonly Dictionary<GroupExpression, MapExpression> _owningMaps = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(object Parent, string Property),
        (TargetExpression? First, TargetExpression? Last, int FirstCount, List<IElement> LastItems)> _targetOrder = new();

    /// <summary>
    /// Creates a new MappingEvaluator instance.
    /// </summary>
    /// <param name="enableFhirPath">Whether to enable FhirPath integration</param>
    public MappingEvaluator(bool enableFhirPath = true)
    {
        _options = MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = enableFhirPath ? new() : null;
        _importResolver = null;
    }

    /// <summary>
    /// Creates a new MappingEvaluator instance with options.
    /// </summary>
    /// <param name="options">Configuration options for the evaluator</param>
    public MappingEvaluator(MappingEvaluatorOptions? options)
    {
        _options = options ?? MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = new FhirPathIntegration();
        _importResolver = null;
    }

    /// <summary>
    /// Creates a new MappingEvaluator instance with import resolution.
    /// </summary>
    /// <param name="enableFhirPath">Whether to enable FhirPath integration</param>
    /// <param name="importResolver">Optional import resolver for cross-map group invocation</param>
    internal MappingEvaluator(bool enableFhirPath, ImportResolver? importResolver)
    {
        _options = MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = enableFhirPath ? new() : null;
        _importResolver = importResolver;
    }

    /// <summary>
    /// Creates a new MappingEvaluator instance with options and import resolution.
    /// </summary>
    /// <param name="options">Configuration options for the evaluator</param>
    /// <param name="importResolver">Optional import resolver for cross-map group invocation</param>
    internal MappingEvaluator(MappingEvaluatorOptions? options, ImportResolver? importResolver)
    {
        _options = options ?? MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = new FhirPathIntegration();
        _importResolver = importResolver;
    }

    /// <summary>
    /// Creates a new MappingEvaluator instance with mutation support.
    /// </summary>
    /// <param name="options">Configuration options for the evaluator</param>
    /// <param name="mutator">Optional mutator for applying changes to target resources</param>
    public MappingEvaluator(MappingEvaluatorOptions? options, IJsonNodeMutator? mutator)
    {
        _options = options ?? MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = new FhirPathIntegration();
        _mutator = mutator;
        _importResolver = null;
    }

    /// <summary>
    /// Creates a new MappingEvaluator instance with mutation support and import resolution.
    /// </summary>
    /// <param name="options">Configuration options for the evaluator</param>
    /// <param name="mutator">Optional mutator for applying changes to target resources</param>
    /// <param name="importResolver">Optional import resolver for cross-map group invocation</param>
    internal MappingEvaluator(MappingEvaluatorOptions? options, IJsonNodeMutator? mutator, ImportResolver? importResolver)
    {
        _options = options ?? MappingEvaluatorOptions.Default;
        _options.Validate();
        _fhirPathIntegration = new FhirPathIntegration();
        _mutator = mutator;
        _importResolver = importResolver;
    }

    /// <summary>
    /// Resets the evaluator state for reuse.
    /// Clears recursion depth, element creation count, and error tracking.
    /// </summary>
    public void Reset()
    {
        _recursionDepth = 0;
        _elementsCreated = 0;
        _currentGroupName = null;
        _currentRuleIndex = null;
        _currentRuleName = null;
        _sourceRoots.Clear();
        _targetRoots.Clear();
        _sourceSnapshots.Clear();
    }

    /// <summary>
    /// Executes a map expression to transform source resources to target resources.
    /// </summary>
    /// <param name="map">The parsed map expression</param>
    /// <param name="context">The evaluation context with sources and targets</param>
    public void Execute(MapExpression map, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Groups.Count == 0)
        {
            throw new InvalidOperationException("The map has no entry group.");
        }
        ExecuteGroup(map, map.Groups[0].Name, context);
    }

    /// <summary>
    /// Executes a specific group by name with provided arguments.
    /// </summary>
    public void ExecuteGroup(MapExpression map, string groupName, MappingContext context)
    {
        _currentMap = map;
        Reset();
        _sharedTargets.Clear();
        _intermediateTargets.Clear();
        _singleTargets.Clear();
        _targetOrder.Clear();
        _resolvedGroups.Clear();
        _owningMaps.Clear();
        foreach (var source in context.Sources)
        {
            if (source.Value.Meta<JsonNode>() is { } node)
                _sourceRoots.Add(JsonRoot(node));
        }
        foreach (var target in context.Targets)
        {
            if (target.Value.Meta<JsonNode>() is { } node)
            {
                var root = JsonRoot(node);
                _sourceRoots.Remove(root);
                _targetRoots.Add(root);
            }
        }

        // Wire up standard transforms if not already configured
        context.TransformResolver ??= (name, args) =>
        {
            var transform = StandardTransforms.Get(name);
            if (transform == null)
            {
                throw new InvalidOperationException($"Transform function '{name}' not found");
            }
            return transform.Execute(args.ToList(), context);
        };

        // Wire up FhirPath evaluator if enabled and not already configured
        if (_fhirPathIntegration != null && context.FhirPathEvaluator == null)
        {
            context.FhirPathEvaluator = (expression, element) => _fhirPathIntegration.Evaluate(expression, element, context);
        }

        context.ClearConstants();
        foreach (var constant in map.Constants)
        {
            context.SetConstant(constant.Name, () => VisitExpression(constant.Value, context).ToList());
        }

        var group = map.Groups.FirstOrDefault(g => g.Name == groupName);
        if (group == null)
        {
            throw new InvalidOperationException($"Group '{groupName}' not found in map");
        }

        VisitGroup(group, map, context);
        if (_options.PruneEmptyTargets)
        {
            foreach (var root in _targetRoots)
                PruneEmptyElements(root);
        }
    }

    private static bool PruneEmptyElements(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string name in obj.Select(property => property.Key).ToArray())
                {
                    if (!obj.TryGetPropertyValue(name, out var child))
                        continue;

                    if (name.StartsWith('_') && obj[name[1..]] is JsonArray)
                        continue;

                    if (child is JsonArray)
                    {
                        string valueName = name.StartsWith('_') ? name[1..] : name;
                        if (obj["_" + valueName] is JsonArray shadows)
                        {
                            PrunePrimitiveArray(obj, valueName, obj[valueName] as JsonArray, shadows);
                            continue;
                        }
                    }

                    if (!PruneEmptyElements(child))
                        obj.Remove(name);
                }
                return obj.Count > 0;
            case JsonArray array:
                for (int index = array.Count - 1; index >= 0; index--)
                {
                    if (!PruneEmptyElements(array[index]))
                        array.RemoveAt(index);
                }
                return array.Count > 0;
            default:
                return node is not null;
        }
    }

    private static void PrunePrimitiveArray(JsonObject owner, string name, JsonArray? values, JsonArray shadows)
    {
        // FHIR primitive values and metadata must keep matching indices, including null placeholders.
        int count = Math.Max(values?.Count ?? 0, shadows.Count);
        while (values is not null && values.Count < count)
            values.Add(null);
        while (shadows.Count < count)
            shadows.Add(null);

        for (int index = count - 1; index >= 0; index--)
        {
            bool hasValue = values is not null && PruneEmptyElements(values[index]);
            bool hasChildren = PruneEmptyElements(shadows[index]);
            if (!hasValue && !hasChildren)
            {
                values?.RemoveAt(index);
                shadows.RemoveAt(index);
            }
            else
            {
                if (values is not null && !hasValue)
                    values[index] = null;
                if (!hasChildren)
                    shadows[index] = null;
            }
        }

        if (values is not null && values.All(value => value is null))
            owner.Remove(name);
        if (shadows.All(shadow => shadow is null))
            owner.Remove("_" + name);
    }

    private void VisitGroup(GroupExpression group, MapExpression map, MappingContext context, HashSet<GroupExpression>? visitedGroups = null)
    {
        var location = $"Group: {group.Name}";
        var previousGroupName = _currentGroupName;
        var previousMap = _currentMap;
        _currentMap = map;
        _recursionDepth++;

        try
        {
            // Check recursion limit
            if (_recursionDepth > _options.MaxRecursionDepth)
            {
                throw new MappingExecutionException(
                    $"Maximum recursion depth exceeded ({_options.MaxRecursionDepth}). " +
                    $"Check for circular group calls in group '{group.Name}'.",
                    location,
                    "MAX_RECURSION_EXCEEDED");
            }

            // Set current group for error tracking
            _currentGroupName = group.Name;

            // Initialize visited groups set for circular inheritance detection
            visitedGroups ??= new HashSet<GroupExpression>(ReferenceEqualityComparer.Instance);

            // Check for circular inheritance
            if (!visitedGroups.Add(group))
            {
                throw new InvalidOperationException(
                    $"Circular group inheritance detected: group '{group.Name}' is part of an inheritance cycle");
            }

            // Handle group inheritance (extends)
            if (!string.IsNullOrEmpty(group.Extends))
            {
                var baseGroup = FindGroup(map, group.Extends);

                if (baseGroup == null)
                {
                    throw new InvalidOperationException(
                        $"Group '{group.Name}' extends '{group.Extends}', but base group not found");
                }

                // Execute base group first (recursive, handles transitive inheritance)
                var baseMap = FindOwningMap(map, baseGroup);
                VisitGroup(baseGroup, baseMap, context, visitedGroups);
            }

            // Then execute this group's own rules

            // Validate that all required parameters are provided
            foreach (var param in group.Parameters)
            {
                if (param.Mode == ParameterMode.Source)
                {
                    if (context.GetSource(param.Name) == null)
                    {
                        throw new MappingExecutionException($"Required source parameter '{param.Name}' not provided", location, "MISSING_PARAMETER");
                    }
                }
                else if (param.Mode == ParameterMode.Target)
                {
                    if (context.GetTarget(param.Name) == null)
                    {
                        throw new MappingExecutionException($"Required target parameter '{param.Name}' not provided", location, "MISSING_PARAMETER");
                    }
                }
            }

            // Execute each rule in the group
            for (int i = 0; i < group.Rules.Count; i++)
            {
                _currentRuleIndex = i;
                VisitRule(group.Rules[i], context, group.Name);
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            CheckErrorLimit(context);
            context.AddError($"Error executing group: {ex.Message}", location, "GROUP_EXECUTION_ERROR", ex, groupName: group.Name);
        }
        finally
        {
            // Restore previous group context
            _currentGroupName = previousGroupName;
            _currentMap = previousMap;
            _currentRuleIndex = null;
            _recursionDepth--;
        }
    }

    private void VisitRule(RuleExpression rule, MappingContext context, string? groupName = null)
    {
        var ruleName = !string.IsNullOrEmpty(rule.Name) ? rule.Name : "anonymous";
        var location = groupName != null ? $"Group: {groupName}, Rule: {ruleName}" : $"Rule: {ruleName}";
        var previousRuleName = _currentRuleName;

        try
        {
            // Set current rule for error tracking
            _currentRuleName = ruleName;

            ExecuteRuleSources(rule, context, 0, new Dictionary<string, IEnumerable<IElement>>(), groupName, location);
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            CheckErrorLimit(context);
            context.AddError(
                $"Error executing rule: {ex.Message}",
                location,
                "RULE_EXECUTION_ERROR",
                ex,
                ruleName: ruleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
        }
        finally
        {
            // Restore previous rule context
            _currentRuleName = previousRuleName;
        }
    }

    private void ExecuteRuleSources(
        RuleExpression rule, MappingContext context, int index,
        Dictionary<string, IEnumerable<IElement>> sources, string? groupName, string location)
    {
        if (index < rule.Sources.Count)
        {
            var source = rule.Sources[index];
            var values = VisitSource(source, context, location).ToList();
            if (values.Count > 0 && rule.Targets.Any(t => t.ListMode == ListMode.OnlyOne))
            {
                values = ValidateOnlyOne(values).ToList();
            }
            foreach (var value in values)
            {
                if (source.Variable is not null)
                {
                    context.SetVariable(source.Variable, value);
                }
                sources[source.Variable ?? $"__anonymous_{index}"] = [value];
                var previousFocus = context.Focus;
                context.Focus = value;
                try
                {
                    ExecuteRuleSources(rule, context, index + 1, sources, groupName, location);
                }
                finally
                {
                    context.Focus = previousFocus;
                }
            }
            return;
        }

        var previousIntermediates = _intermediateTargets;
        _intermediateTargets = new();
        try
        {
            foreach (var target in rule.Targets)
            {
                VisitTarget(target, context, sources, location);
            }
            if (rule.Dependent is not null)
            {
                VisitDependentExpression(rule.Dependent, context, groupName, location);
            }
        }
        finally
        {
            _intermediateTargets = previousIntermediates;
        }
    }

    private void VisitDependentExpression(Expression dependent, MappingContext context, string? groupName, string? location)
    {
        try
        {
            switch (dependent)
            {
                case RuleSetExpression ruleSet:
                    VisitRuleSet(ruleSet, context, groupName);
                    break;

                case GroupInvocationExpression groupInvocation:
                    // Execute group invocation
                    VisitGroupInvocation(groupInvocation, context, location);
                    break;

                case DependentExpression clauses:
                    foreach (var invocation in clauses.Invocations)
                    {
                        VisitGroupInvocation(invocation, context, location);
                    }
                    if (clauses.Rules is not null)
                    {
                        VisitDependentExpression(clauses.Rules, context, groupName, location);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Dependent expression type {dependent.GetType().Name} not supported");
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Error executing dependent expression: {ex.Message}",
                location,
                "DEPENDENT_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
        }
    }

    private void VisitRuleSet(RuleSetExpression ruleSet, MappingContext context, string? groupName)
    {
        var parentScope = _sharedTargets;
        // Inherit existing target identities, but keep newly declared child bindings local.
        _sharedTargets = new(parentScope, StringComparer.Ordinal);
        try
        {
            foreach (var rule in ruleSet.Rules)
            {
                VisitRule(rule, context, groupName);
            }
        }
        finally
        {
            _sharedTargets = parentScope;
        }
    }

    private GroupExpression? FindGroup(MapExpression map, string groupName)
    {
        var key = (map, groupName);
        if (_resolvedGroups.TryGetValue(key, out var resolved))
        {
            return resolved;
        }

        resolved = map.Groups.FirstOrDefault(group => string.Equals(group.Name, groupName, StringComparison.Ordinal))
            ?? _importResolver?.FindGroup(map, groupName);
        _resolvedGroups[key] = resolved;
        return resolved;
    }

    private MapExpression FindOwningMap(MapExpression currentMap, GroupExpression group)
    {
        if (currentMap.Groups.Contains(group))
        {
            return currentMap;
        }

        if (_owningMaps.TryGetValue(group, out var owningMap))
        {
            return owningMap;
        }

        owningMap = _importResolver!.FindOwningMap(group);
        _owningMaps[group] = owningMap;
        return owningMap;
    }

    private void VisitGroupInvocation(GroupInvocationExpression invocation, MappingContext context, string? location)
    {
        try
        {
            if (_currentMap == null)
            {
                throw new InvalidOperationException("Cannot invoke group - no map context available");
            }

            var targetGroup = FindGroup(_currentMap, invocation.GroupName);

            if (targetGroup == null)
            {
                throw new InvalidOperationException($"Group '{invocation.GroupName}' not found in map or imports");
            }

            // Validate argument count matches parameter count
            if (invocation.Arguments.Count != targetGroup.Parameters.Count)
            {
                throw new InvalidOperationException(
                    $"Group '{invocation.GroupName}' expects {targetGroup.Parameters.Count} parameters " +
                    $"but {invocation.Arguments.Count} arguments were provided");
            }

            // Create a new context scope for the group invocation
            // We need to map arguments to parameter names
            var originalSources = new Dictionary<string, IElement?>();
            var originalTargets = new Dictionary<string, IElement?>();
            var originalVariables = context.CaptureVariables();
            var originalShared = _sharedTargets;
            var originalSingle = _singleTargets;
            var arguments = invocation.Arguments.Select(a => VisitExpression(a, context, location).Single()).ToArray();
            var originalMap = _currentMap;

            _sharedTargets = new(StringComparer.Ordinal);
            _singleTargets = new();
            try
            {
                _currentMap = FindOwningMap(_currentMap, targetGroup);

                // Save current parameter values and set new ones from arguments
                for (int i = 0; i < targetGroup.Parameters.Count; i++)
                {
                    var param = targetGroup.Parameters[i];
                    var arg = invocation.Arguments[i];

                    // Evaluate the argument
                    var argValue = arguments[i];

                    if (argValue == null)
                    {
                        throw new InvalidOperationException(
                            $"Argument {i + 1} for parameter '{param.Name}' evaluated to null");
                    }

                    // Save original value and set new value
                    if (param.Mode == ParameterMode.Source)
                    {
                        originalSources[param.Name] = context.GetSource(param.Name);
                        context.SetSource(param.Name, argValue);
                    }
                    else if (param.Mode == ParameterMode.Target)
                    {
                        originalTargets[param.Name] = context.GetTarget(param.Name);
                        context.SetTarget(param.Name, argValue);
                    }
                }

                // Execute the group
                VisitGroup(targetGroup, _currentMap, context);
            }
            finally
            {
                _currentMap = originalMap;
                context.RestoreVariables(originalVariables);
                _sharedTargets = originalShared;
                _singleTargets = originalSingle;
                // Restore original parameter values
                foreach (var kvp in originalSources)
                {
                    if (kvp.Value != null)
                    {
                        context.SetSource(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        context.RemoveSource(kvp.Key);
                    }
                }

                foreach (var kvp in originalTargets)
                {
                    if (kvp.Value != null)
                    {
                        context.SetTarget(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        context.RemoveTarget(kvp.Key);
                    }
                }
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Error invoking group '{invocation.GroupName}': {ex.Message}",
                location,
                "GROUP_INVOCATION_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
        }
    }

    private IEnumerable<IElement> VisitSource(SourceExpression source, MappingContext context, string? location = null)
    {
        try
        {
            // Visit the source context expression (materialize to avoid double enumeration)
            IEnumerable<IElement> contextValues = VisitExpression(source.Context, context, location).ToList();

            if (source.Type is not null)
            {
                var type = ResolveTypeName(source.Type);
                contextValues = contextValues.Where(e => e.InstanceType == type).ToList();
            }

            // Apply default value if source is empty and default is specified
            if (!contextValues.Any() && source.Default != null)
            {
                // Evaluate the default expression
                contextValues = VisitExpression(source.Default, context, location).ToList();
            }

            // Apply where condition if present
            if (source.Condition != null && source.Condition is FhirPathExpression fhirPathCondition)
            {
                contextValues = contextValues.Where(element =>
                {
                    try
                    {
                        var result = EvaluateSourceExpression(fhirPathCondition, source, element, context).ToList();
                        return result.Count == 1 && result[0].Value is true;
                    }
                    catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                    {
                        context.AddError($"Error evaluating where condition: {ex.Message}",
                            location, "WHERE_ERROR", ex, ruleName: _currentRuleName,
                            groupName: _currentGroupName, ruleIndex: _currentRuleIndex);
                        return false;
                    }
                }).ToList();
            }

            // Check cardinality constraint if present
            if (source.Cardinality != null)
            {
                var contextList = contextValues.ToList();
                var count = contextList.Count;

                if (!source.Cardinality.IsSatisfiedBy(count))
                {
                    // Build helpful error message with available elements if we have 0 elements
                    string message;
                    IReadOnlyList<string>? availableElements = null;
                    string? elementPath = null;

                    if (count == 0 && source.Context is QualifiedIdentifierExpression qual)
                    {
                        // Try to get the parent element to show what's available
                        var parentElements = VisitExpression(qual.Context, context).ToList();
                        if (parentElements.Any())
                        {
                            var firstParent = parentElements.First();
                            availableElements = firstParent.Children()
                                .Select(c => c.Name)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Distinct()
                                .OrderBy(n => n)
                                .ToList();

                            elementPath = BuildExpressionString(qual);
                            message = $"Source element '{qual.Property}' not found (cardinality {source.Cardinality} requires at least 1)";
                        }
                        else
                        {
                            message = $"Cardinality constraint {source.Cardinality} not satisfied: found {count} element(s)";
                        }
                    }
                    else
                    {
                        message = $"Cardinality constraint {source.Cardinality} not satisfied: found {count} element(s)";
                    }

                    CheckErrorLimit(context);
                    context.AddError(
                        message,
                        location,
                        "CARDINALITY_ERROR",
                        null,
                        ruleName: _currentRuleName,
                        elementPath: elementPath,
                        availableElements: availableElements,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                }

                // Use the materialized list for further processing
                contextValues = contextList;
            }

            // Materialize contextValues to a list to prevent multiple enumerations
            // If already a List (most common case), this is a no-op cast
            var contextValuesList = ApplyListModeFiltering(contextValues.ToList(), source.ListMode).ToList();

            // Apply check condition if present
            if (source.Check != null && source.Check is FhirPathExpression fhirPathCheck)
            {
                // Check conditions are evaluated against the mapping context, not individual elements
                // This allows expressions like "src.name.count() > 0" to work correctly
                try
                {
                    if (context.FhirPathEvaluator == null)
                    {
                        throw new InvalidOperationException("FhirPathEvaluator not configured in context");
                    }

                    foreach (var element in contextValuesList)
                    {
                        var checkResult = EvaluateSourceExpression(fhirPathCheck, source, element, context).ToList();
                        if (checkResult.Count != 1 || checkResult[0].Value is not true)
                        {
                            throw new InvalidOperationException($"Check condition failed: {fhirPathCheck.PathExpression}");
                        }
                    }
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    context.AddError(
                        $"Check condition failed: {ex.Message}",
                        location,
                        "CHECK_ERROR",
                        ex,
                        ruleName: _currentRuleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                }
            }

            // Execute log statement if present (only if we have values to log)
            if (source.Log != null && contextValuesList.Any())
            {
                try
                {
                    // Check if the log expression matches the source context expression
                    // If so, just log the contextValues directly (which may include defaults)
                    bool logMatchesContext = false;

                    if (source.Log is FhirPathExpression fhirPathLog)
                    {
                        // Check for simple identifier match (e.g., log src when context is src)
                        if (source.Context is IdentifierExpression id && fhirPathLog.PathExpression == id.Name)
                        {
                            logMatchesContext = true;
                        }
                        // Check for qualified identifier match (e.g., log src.name when context is src.name)
                        else if (source.Context is QualifiedIdentifierExpression qual &&
                                 qual.Context is IdentifierExpression qualId &&
                                 fhirPathLog.PathExpression == $"{qualId.Name}.{qual.Property}")
                        {
                            logMatchesContext = true;
                        }
                        // Check for indexed expression match (e.g., log src.name[0] when context is src.name[0])
                        else if (source.Context is IndexExpression idx)
                        {
                            // Build the string representation of the index expression
                            var contextStr = BuildExpressionString(idx);
                            if (fhirPathLog.PathExpression == contextStr)
                            {
                                logMatchesContext = true;
                            }
                        }
                    }

                    if (logMatchesContext)
                    {
                        // Log expression matches source context - log the actual values (including defaults)
                        var logMessage = FormatLogResult(contextValuesList);
                        if (context.Logger != null)
                        {
                            context.Logger(logMessage);
                        }
                    }
                    else
                    {
                        // Log expression is different - evaluate it for each element
                        foreach (var element in contextValuesList)
                        {
                            IEnumerable<IElement> logResult;

                            if (source.Log is FhirPathExpression logExpr)
                            {
                                // Evaluate as a mapping expression first (handles variables like "src")
                                logResult = EvaluateSourceExpression(logExpr, source, element, context);
                            }
                            else
                            {
                                // For other expression types, evaluate in the mapping context
                                logResult = VisitExpression(source.Log, context, location);
                            }

                            // Format and log the result
                            var logMessage = FormatLogResult(logResult);

                            if (context.Logger != null)
                            {
                                context.Logger(logMessage);
                            }
                        }
                    }
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    context.AddError(
                        $"Error executing log statement: {ex.Message}",
                        location,
                        "LOG_ERROR",
                        ex,
                        ruleName: _currentRuleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                }
            }

            // Set variable if specified
            if (source.Variable != null)
            {
                foreach (var element in contextValuesList)
                {
                    context.SetVariable(source.Variable, element);
                }
            }

            return contextValuesList;
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            CheckErrorLimit(context);
            context.AddError(
                $"Error visiting source: {ex.Message}",
                location,
                "SOURCE_VISIT_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
            return [];
        }
    }

    private IEnumerable<IElement> EvaluateSourceExpression(
        FhirPathExpression expression, SourceExpression source, IElement element, MappingContext context)
    {
        var previousFocus = context.Focus;
        context.Focus = element;
        if (source.Variable is not null)
        {
            context.SetVariable(source.Variable, element);
        }
        IElement? original = null;
        string? rootName = null;
        if (source.Default is not null &&
            source.Context is QualifiedIdentifierExpression { Context: IdentifierExpression root } property)
        {
            original = context.GetSource(root.Name);
            if (original is not null)
            {
                rootName = root.Name;
                context.SetSource(rootName, new TempPropertyWrapper(original, property.Property, [element]));
            }
        }
        try
        {
            return VisitFhirPath(expression, context).ToList();
        }
        finally
        {
            context.Focus = previousFocus;
            if (rootName is not null && original is not null)
            {
                context.SetSource(rootName, original);
            }
        }
    }

#pragma warning disable CA1303 // Do not pass literals as localized parameters (debug logging)
    private void VisitTarget(TargetExpression target, MappingContext context, Dictionary<string, IEnumerable<IElement>> sourceValues, string? location = null)
    {
        try
        {
            var sourceElement = sourceValues.Values.First().Single();
            IEnumerable<IElement> values;
            if (target.Transform is TransformExpression transform)
            {
                values = NormalizeTransformResult(VisitTransform(transform, context, location, target.Context), transform);
            }
            else if (target.Transform is not null)
            {
                values = VisitExpression(target.Transform, context, location);
            }
            else
            {
                values = [sourceElement];
            }

            foreach (var value in values)
            {
                IElement result = value;
                if (_mutator is IMappingTargetMutator mutation && target.Context is QualifiedIdentifierExpression property)
                {
                    var parent = ResolveTargetParent(property.Context, context, mutation);
                    var sharedId = target.ListRuleId ?? property.Property;
                    if (target.ListMode == ListMode.Single && !_singleTargets.Add(((object?)parent.Meta<JsonNode>() ?? parent, target)))
                    {
                        throw new InvalidOperationException("Target list mode 'single' requires exactly one target instance.");
                    }
                    if (target.ListMode == ListMode.Share && _sharedTargets.TryGetValue(sharedId, out var shared))
                    {
                        result = mutation.Refresh(shared);
                    }
                    else
                    {
                        var defaultGroup = target.Transform is null && target.Variable is null
                            ? FindDefaultGroup(value, parent, mutation.GetTargetDefinition(parent, property.Property))
                            : null;
                        IElement? assigned = target.Transform is null && target.Variable is not null ? null : value;
                        if (defaultGroup is { } binding)
                        {
                            var targetType = binding.Group.Parameters.Single(p => p.Mode == ParameterMode.Target).Type!;
                            assigned = mutation.CreateElement(ResolveTypeName(binding.Map, targetType));
                        }
                        TrackElementCreation();
                        result = mutation.SetTarget(parent, property.Property, assigned);
                        OrderTarget(parent, property.Property, target, result, mutation);
                        if (target.ListMode == ListMode.Share)
                        {
                            _sharedTargets[sharedId] = result;
                        }
                        if (defaultGroup is { } conversion)
                        {
                            InvokeBoundGroup(conversion.Map, conversion.Group, [value, result], context);
                        }
                    }
                }
                else if (_mutator is not null && target.Context is QualifiedIdentifierExpression)
                {
                    MutateTargetResource(target, value, context, location);
                }
                else if (target.Context is IdentifierExpression id && target.Transform is null)
                {
                    result = context.GetTarget(id.Name) ?? value;
                }
                if (target.Variable is not null)
                {
                    context.SetVariable(target.Variable, result);
                }
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Error visiting target: {ex.Message}",
                location,
                "TARGET_VISIT_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
        }
    }
#pragma warning restore CA1303

    private void OrderTarget(IElement parent, string property, TargetExpression target, IElement result, IMappingTargetMutator mutation)
    {
        if (!mutation.GetTargetDefinition(parent, property).IsCollection)
            return;
        var key = ((object?)parent.Meta<JsonNode>() ?? parent, property);
        if (!_targetOrder.TryGetValue(key, out var order))
            order = (null, null, 0, []);
        if (target.ListMode == ListMode.First)
        {
            if (order.First is not null && !ReferenceEquals(order.First, target))
                throw new InvalidOperationException($"Multiple rules specify 'first' for target '{property}'.");
            order.First = target;
            mutation.MoveTarget(result, order.FirstCount++);
        }
        if (target.ListMode == ListMode.Last)
        {
            if (order.Last is not null && !ReferenceEquals(order.Last, target))
                throw new InvalidOperationException($"Multiple rules specify 'last' for target '{property}'.");
            order.Last = target;
            order.LastItems.Add(result);
        }
        foreach (var last in order.LastItems)
        {
            mutation.MoveTarget(last);
        }
        _targetOrder[key] = order;
    }

    private IElement ResolveTargetParent(Expression expression, MappingContext context, IMappingTargetMutator mutation)
    {
        if (expression is QualifiedIdentifierExpression property)
        {
            var parent = ResolveTargetParent(property.Context, context, mutation);
            var key = ((object?)parent.Meta<JsonNode>() ?? parent, property.Property);
            if (!_intermediateTargets.TryGetValue(key, out var intermediate))
            {
                TrackElementCreation();
                intermediate = mutation.SetTarget(parent, property.Property, null);
                _intermediateTargets.Add(key, intermediate);
                return intermediate;
            }
            return mutation.Refresh(intermediate);
        }
        var matches = VisitExpression(expression, context).ToList();
        if (matches.Count == 1)
        {
            var match = matches[0];
            if (match.Meta<JsonNode>() is { } node)
            {
                var root = JsonRoot(node);
                _targetRoots.Add(root);
                // A source can be passed as a target to a dependent group; from that point it is mutable.
                if (_sourceRoots.Remove(root))
                {
                    _sourceSnapshots.Clear();
                    return mutation.Refresh(match);
                }
            }
            return expression is IdentifierExpression ? match : mutation.Refresh(match);
        }
        throw new MappingExecutionException($"Target context '{expression}' resolved to {matches.Count} elements; expected one.",
            code: "TARGET_RESOURCE_NOT_FOUND");
    }

    private string ResolveTypeName(string name) =>
        _currentMap is { } map ? ResolveTypeName(map, name) : name;

    private static string ResolveTypeName(MapExpression map, string name) =>
        map.Uses.FirstOrDefault(use => string.Equals(use.Alias, name, StringComparison.Ordinal))?.Url
            .Split('#', '|')[0].Split('/').Last() ?? name;

    private (MapExpression Map, GroupExpression Group)? FindDefaultGroup(IElement source, IElement targetParent, IType target)
    {
        if (target is not ITypeExtended definition || _currentMap is not { } currentMap)
            return null;

        var sourceSchema = (source as SchemaAwareElement)?.Schema;
        var targetSchema = (targetParent as SchemaAwareElement)?.Schema;
        var lookupKey = (currentMap, source.InstanceType, sourceSchema, target, targetSchema);
        if (_defaultGroupMatches.TryGetValue(lookupKey, out var cachedMatch))
            return cachedMatch;

        var groupsBySourceType = GetDefaultGroupsBySourceType(currentMap);
        if (!groupsBySourceType.TryGetValue(source.InstanceType, out var candidates))
        {
            _defaultGroupMatches.Add(lookupKey, null);
            return null;
        }

        var matches = new List<(MapExpression Map, GroupExpression Group)>();
        foreach (var (map, group) in candidates)
        {
            if (!MatchesDeclaredType(map, group.Parameters[0].Type, ModelMode.Source, source, sourceSchema))
                continue;

            string targetTypeName = ResolveTypeName(map, group.Parameters[1].Type ?? string.Empty);
            bool targetTypeAllowed = definition.Types.Any(type => type.Code == targetTypeName ||
                (group.TypeMode == GroupTypeMode.TypeAndTypes && type.Code is "Resource" or "DomainResource"));
            if (targetTypeAllowed && MatchesDeclaredType(map, group.Parameters[1].Type, ModelMode.Target, targetParent, targetSchema, targetTypeName))
            {
                matches.Add((map, group));
            }
        }

        var exactMatches = matches.Where(match =>
            ResolveTypeName(match.Map, match.Group.Parameters[1].Type ?? string.Empty) == source.InstanceType).ToList();
        var selectedMatches = exactMatches.Count > 0 ? exactMatches : matches;
        (MapExpression Map, GroupExpression Group)? selected = selectedMatches.Count switch
        {
            0 => null,
            1 => selectedMatches[0],
            _ => throw new InvalidOperationException(
                $"Multiple default groups match '{source.InstanceType}' and target '{target.Info.Name}': " +
                string.Join(", ", selectedMatches.Select(match => $"{match.Map.Url}#{match.Group.Name}")))
        };
        _defaultGroupMatches.Add(lookupKey, selected);
        return selected;
    }

    private Dictionary<string, List<(MapExpression Map, GroupExpression Group)>> GetDefaultGroupsBySourceType(MapExpression map)
    {
        if (_defaultGroupsBySourceType.TryGetValue(map, out var cached))
            return cached;

        var groupsBySourceType = new Dictionary<string, List<(MapExpression Map, GroupExpression Group)>>(StringComparer.Ordinal);
        var maps = new List<MapExpression> { map };
        if (_importResolver is not null)
        {
            maps.AddRange(_importResolver.GetImportedMaps(map).Where(imported => !ReferenceEquals(imported, map)));
        }

        foreach (var owner in maps)
        {
            foreach (var group in owner.Groups)
            {
                string? sourceParameterType = group.Parameters.Count == 2 ? group.Parameters[0].Type : null;
                if (group.TypeMode == GroupTypeMode.None || group.Parameters.Count != 2 ||
                    group.Parameters[0].Mode != ParameterMode.Source || group.Parameters[1].Mode != ParameterMode.Target ||
                    string.IsNullOrWhiteSpace(sourceParameterType))
                {
                    continue;
                }

                string sourceType = ResolveTypeName(owner, sourceParameterType);
                if (!groupsBySourceType.TryGetValue(sourceType, out var candidates))
                {
                    candidates = [];
                    groupsBySourceType.Add(sourceType, candidates);
                }
                candidates.Add((owner, group));
            }
        }

        _defaultGroupsBySourceType.Add(map, groupsBySourceType);
        return groupsBySourceType;
    }

    private static bool MatchesDeclaredType(
        MapExpression map,
        string? declaredType,
        ModelMode mode,
        IElement element,
        ISchema? schema,
        string? resolvedTypeName = null)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
            return false;

        string typeName = resolvedTypeName ?? ResolveTypeName(map, declaredType);
        UsesExpression? use = map.Uses.FirstOrDefault(candidate => candidate.Mode == mode &&
            (string.Equals(candidate.Alias, declaredType, StringComparison.Ordinal) ||
             (candidate.Alias is null && GetStructureDefinitionTypeName(candidate.Url) == typeName)));
        if (use is null)
        {
            return mode == ModelMode.Target ||
                   string.Equals(element.InstanceType, typeName, StringComparison.Ordinal) ||
                   string.Equals(element.Type?.Info.Name, typeName, StringComparison.Ordinal);
        }

        string declaredName = GetStructureDefinitionTypeName(use.Url);
        if (!string.Equals(declaredName, typeName, StringComparison.Ordinal))
            return false;

        if (mode == ModelMode.Source)
        {
            if (!string.Equals(element.InstanceType, declaredName, StringComparison.Ordinal))
                return false;

            if (element.Type is ITypeExtended { CanonicalUrl: { } canonicalUrl } &&
                canonicalUrl.IndexOf('#', StringComparison.Ordinal) < 0 &&
                !string.Equals(GetStructureDefinitionTypeName(canonicalUrl), declaredName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        FhirVersion? declaredVersion = GetFhirVersion(use.Url);
        return declaredVersion is null || schema is null || schema.Version == declaredVersion;
    }

    private static string GetStructureDefinitionTypeName(string url)
    {
        string canonical = url.Split('|')[0].Split('#')[0];
        int marker = canonical.LastIndexOf("/StructureDefinition/", StringComparison.Ordinal);
        return marker >= 0 ? canonical[(marker + "/StructureDefinition/".Length)..] : canonical.Split('/').Last();
    }

    private static FhirVersion? GetFhirVersion(string url)
    {
        string[] segments = url.Split('|')[0].Split('#')[0].Split('/');
        int marker = Array.IndexOf(segments, "StructureDefinition");
        if (marker <= 0)
            return null;

        return segments[marker - 1] switch
        {
            "3.0" => FhirVersion.Stu3,
            "4.0" => FhirVersion.R4,
            "4.3" => FhirVersion.R4B,
            "5.0" => FhirVersion.R5,
            "6.0" => FhirVersion.R6,
            _ => null
        };
    }

    private void InvokeBoundGroup(MapExpression map, GroupExpression group, IReadOnlyList<IElement> arguments, MappingContext context)
    {
        var savedSources = group.Parameters.Select(p => context.GetSource(p.Name)).ToArray();
        var savedTargets = group.Parameters.Select(p => context.GetTarget(p.Name)).ToArray();
        var savedVariables = context.CaptureVariables();
        var savedShared = _sharedTargets;
        var savedSingle = _singleTargets;
        var savedMap = _currentMap;
        _currentMap = map;
        _sharedTargets = new(StringComparer.Ordinal);
        _singleTargets = new();
        try
        {
            for (var i = 0; i < group.Parameters.Count; i++)
            {
                var parameter = group.Parameters[i];
                if (parameter.Mode == ParameterMode.Source)
                    context.SetSource(parameter.Name, arguments[i]);
                else
                    context.SetTarget(parameter.Name, arguments[i]);
            }
            VisitGroup(group, map, context);
        }
        finally
        {
            context.RestoreVariables(savedVariables);
            _sharedTargets = savedShared;
            _singleTargets = savedSingle;
            _currentMap = savedMap;
            for (var i = 0; i < group.Parameters.Count; i++)
            {
                var name = group.Parameters[i].Name;
                if (savedSources[i] is { } source) context.SetSource(name, source);
                else context.RemoveSource(name);
                if (savedTargets[i] is { } target) context.SetTarget(name, target);
                else context.RemoveTarget(name);
            }
        }
    }

    private IEnumerable<IElement> NormalizeTransformResult(object? value, TransformExpression transform)
    {
        if (value is null) return [];
        if (value is IElement element) return [element];
        if (value is IEnumerable<IElement> elements) return elements;
        if (value is IDictionary or JsonNode)
        {
            var type = transform.FunctionName switch
            {
                "cc" => "CodeableConcept",
                "c" => "Coding",
                "qty" => "Quantity",
                "id" => "Identifier",
                "cp" => "ContactPoint",
                "translate" when transform.Arguments.Count > 0 => (transform.Arguments[^1] as LiteralExpression)?.Value as string,
                _ => null
            };
            if (_mutator is not IMappingTargetMutator mutation || type is null)
                throw new InvalidOperationException($"A schema-aware mutator is required for '{transform.FunctionName}' results.");
            return [mutation.CreateElement(type, value)];
        }
        return [CreatePrimitive(value)];
    }

    private IEnumerable<IElement> ApplyListModeFiltering(IReadOnlyList<IElement> elements, ListMode? listMode)
    {
        if (!listMode.HasValue || elements.Count == 0)
        {
            return elements;
        }

        return listMode.Value switch
        {
            ListMode.First => elements.Take(1),
            ListMode.NotFirst => elements.Skip(1),
            ListMode.Last => elements.Skip(elements.Count - 1),
            ListMode.NotLast => elements.Take(elements.Count - 1),
            ListMode.OnlyOne => ValidateOnlyOne(elements),
            ListMode.Share => elements, // Share means use the same target - handled differently
            ListMode.Single => elements.Take(1), // Single creates one target regardless of source count
            _ => throw new NotSupportedException($"List mode {listMode.Value} not yet implemented")
        };
    }

    private IEnumerable<IElement> ValidateOnlyOne(IReadOnlyList<IElement> elements)
    {
        if (elements.Count != 1)
        {
            throw new InvalidOperationException(
                $"List mode 'only_one' requires exactly one element, but found {elements.Count}");
        }

        return elements;
    }

    private object? VisitTransform(TransformExpression transform, MappingContext context, string? location = null, Expression? target = null)
    {
        try
        {
            if (context.TransformResolver == null)
            {
                throw new InvalidOperationException("TransformResolver not configured in context");
            }

            if (transform.FunctionName == "evaluate")
            {
                if (transform.Arguments.Count != 2)
                    throw new ArgumentException("evaluate() requires source and expression arguments.");
                var results = new List<IElement>();
                foreach (var focus in VisitExpression(transform.Arguments[0], context, location).ToList())
                {
                    var previousFocus = context.Focus;
                    context.Focus = focus;
                    try
                    {
                        results.AddRange(VisitExpression(transform.Arguments[1], context, location));
                    }
                    finally
                    {
                        context.Focus = previousFocus;
                    }
                }
                return results;
            }

            // Visit arguments
            List<object> args = [];
            foreach (var arg in transform.Arguments)
            {
                try
                {
                    var argValues = VisitExpression(arg, context, location).ToList();
                    if (argValues.Count == 0)
                        return null;
                    if (argValues.Count != 1)
                        throw new InvalidOperationException($"Transform '{transform.FunctionName}' requires singleton arguments.");
                    var argValue = argValues[0];
                    args.Add(transform.FunctionName is "reference" or "pointer" or "cast" ? argValue : argValue.Value ?? argValue);
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    context.AddError(
                        $"Error evaluating transform argument: {ex.Message}",
                        location,
                        "TRANSFORM_ARG_ERROR",
                        ex,
                        ruleName: _currentRuleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                    // Continue with other arguments
                }
            }

            if (transform.FunctionName == "cast" && transform.Arguments.Count == 1 && args.Count == 1)
            {
                if (_mutator is not IMappingTargetMutator mutation)
                    throw new InvalidOperationException("Inferring a cast type requires a schema-aware mapping mutator.");
                if (target is QualifiedIdentifierExpression property)
                {
                    var parent = ResolveTargetParent(property.Context, context, mutation);
                    args.Add(mutation.GetTargetType(parent, property.Property));
                }
                else
                {
                    var targets = target is null ? [] : VisitExpression(target, context, location).ToList();
                    if (targets.Count != 1)
                        throw new InvalidOperationException("cast() requires an explicit type when no single target type is known.");
                    args.Add(targets[0].InstanceType);
                }
            }

            if (transform.FunctionName == "translate" && args.Count == 3 &&
                _currentMap?.ConceptMaps.FirstOrDefault(c => c.Identifier.TrimStart('#') == args[1].ToString()!.TrimStart('#')) is { } conceptMap)
            {
                return TranslateInline(conceptMap, args[0], args[2].ToString()!);
            }

            // Call the transform function
            var result = context.TransformResolver(transform.FunctionName, args);
            if (transform.FunctionName == "cast" && result is not null && args.Count == 2 &&
                CastTransform.GetFhirTypeName(TransformValue.String(args[1])) is { } targetType)
            {
                if (TransformValue.String(args[1]).StartsWith("System.", StringComparison.OrdinalIgnoreCase))
                {
                    var systemValue = result is IElement element
                        ? element.Value ?? throw new InvalidOperationException("A System cast must produce a value.")
                        : result;
                    TrackElementCreation();
                    return systemValue is FhirQuantity systemQuantity
                        ? FhirPathIntegration.CreateSystemQuantity(systemQuantity)
                        : new PrimitiveElement(systemValue, targetType == "integer64" ? "long" : targetType);
                }
                if (_mutator is not IMappingTargetMutator mutation)
                    throw new InvalidOperationException("Casting to a FHIR type requires a schema-aware mapping mutator.");

                return mutation.CreateElement(targetType, result is FhirQuantity quantity ? CastTransform.ToFhirQuantity(quantity) : result);
            }
            return result;
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Error executing transform '{transform.FunctionName}': {ex.Message}",
                location,
                "TRANSFORM_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
            return null;
        }
    }

    private static object? TranslateInline(ConceptMapDeclarationExpression map, object source, string output)
    {
        var code = source is IElement element
            ? element.Value?.ToString() ?? element.Children("code").SingleOrDefault()?.Value?.ToString()
            : source.ToString();
        var translation = map.Groups.SelectMany(g => g.CodeMaps)
            .SingleOrDefault(m => m.SourceCode == code && m.Equivalence != ConceptMapEquivalence.NotRelatedTo);
        if (translation is null)
            return null;
        var system = map.Prefixes.Single(p => p.PrefixName == translation.TargetPrefix).Url;
        var coding = new Dictionary<string, object> { ["system"] = system, ["code"] = translation.TargetCode };
        return output switch
        {
            "code" => translation.TargetCode,
            "system" => system,
            "display" => null,
            "Coding" => coding,
            "CodeableConcept" => new Dictionary<string, object> { ["coding"] = new[] { coding } },
            _ => throw new ArgumentException($"Unsupported translate() output '{output}'.")
        };
    }

    private IEnumerable<IElement> VisitExpression(Expression expr, MappingContext context, string? location = null)
    {
        try
        {
            return expr switch
            {
                IdentifierExpression id => VisitIdentifier(id, context),
                QualifiedIdentifierExpression qual => VisitQualifiedIdentifier(qual, context),
                IndexExpression idx => VisitIndex(idx, context),
                LiteralExpression lit => new[] { CreatePrimitive(lit.Value) },
                FhirPathExpression fhirPath => VisitFhirPath(fhirPath, context),
                _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} not supported in this context")
            };
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Error evaluating expression: {ex.Message}",
                location,
                "EXPRESSION_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
            return [];
        }
    }

    private IEnumerable<IElement> VisitIdentifier(IdentifierExpression id, MappingContext context)
    {
        // Check if it's a source
        var source = context.GetSource(id.Name);
        if (source != null)
        {
            return new[] { RefreshElement(source) };
        }

        // Check if it's a target
        var target = context.GetTarget(id.Name);
        if (target != null)
        {
            return new[] { RefreshElement(target) };
        }

        // Check if it's a variable
        var variable = context.GetVariable(id.Name);
        if (variable is IElement element)
        {
            return new[] { RefreshElement(element) };
        }

        if (variable is IEnumerable<IElement> elements)
        {
            return elements.Select(RefreshElement).ToList();
        }

        return [];
    }

    private IEnumerable<IElement> VisitQualifiedIdentifier(QualifiedIdentifierExpression qual, MappingContext context)
    {
        // Visit the context first
        var contextElements = VisitExpression(qual.Context, context).ToList();

        if (!contextElements.Any())
        {
            return [];
        }

        // Navigate to the property and collect all children
        var result = new List<IElement>();
        foreach (var element in contextElements)
        {
            bool isPrimitive = IsPrimitiveElement(element) ||
                element.InstanceType == "xhtml" && element.HasPrimitiveValue;
            if (qual.Property == "value" && isPrimitive)
            {
                if (element.HasPrimitiveValue && element.Value is { } value)
                    result.Add(CreatePrimitive(value, trackCreation: false));
                continue;
            }

            result.AddRange(element.Children(qual.Property));
        }

        // Don't add error here - let VisitSource handle it based on cardinality constraints
        // If cardinality allows 0, an empty result is valid
        return result;
    }

    private IEnumerable<IElement> VisitIndex(IndexExpression idx, MappingContext context)
    {
        // Visit the context first
        var contextElements = VisitExpression(idx.Context, context).ToList();

        // Check bounds
        if (idx.Index < 0 || idx.Index >= contextElements.Count)
        {
            // Index out of bounds - return empty
            yield break;
        }

        // Return the element at the specified index
        yield return contextElements[idx.Index];
    }

    private IEnumerable<IElement> VisitFhirPath(FhirPathExpression fhirPath, MappingContext context)
    {
        if (context.FhirPathEvaluator == null)
        {
            throw new InvalidOperationException("FhirPathEvaluator not configured in context");
        }

        MappingContextElement contextRoot = context.Focus is ISystemValueElement
            ? new SystemMappingContextElement(context, RefreshElement)
            : new MappingContextElement(context, RefreshElement);
        return context.FhirPathEvaluator(fhirPath.PathExpression, contextRoot)
            .Select(element => ReferenceEquals(element, contextRoot) && contextRoot.Focus is { } focus
                ? RefreshElement(focus)
                : element);
    }

    private IElement RefreshElement(IElement element)
    {
        if (_mutator is not IMappingTargetMutator mutation || element.Meta<JsonNode>() is not { } node)
            return element;
        if (!_sourceRoots.Contains(JsonRoot(node)))
            return mutation.Refresh(element);
        if (_sourceSnapshots.TryGetValue(element, out var snapshot))
            return snapshot;

        // Preserve the mutator's schema interpretation, but reuse it while the source is unchanged.
        snapshot = mutation.Refresh(element);
        _sourceSnapshots.Add(element, snapshot);
        _sourceSnapshots.TryAdd(snapshot, snapshot);
        return snapshot;
    }

    private static bool IsPrimitiveElement(IElement element)
    {
        if (element is not SchemaAwareElement schemaAware)
            return element.Type?.Info.IsPrimitive == true;

        var instanceType = schemaAware.Schema.GetTypeDefinition(element.InstanceType);
        return instanceType?.Info.IsPrimitive ??
            (element.Type is { } definition && definition.Info.Name == element.InstanceType && definition.Info.IsPrimitive);
    }

    private static JsonNode JsonRoot(JsonNode node)
    {
        while (node.Parent is { } parent)
            node = parent;
        return node;
    }

    private IElement CreatePrimitive(object value, bool trackCreation = true)
    {
        if (trackCreation)
            TrackElementCreation();

        var typeName = value switch
        {
            string => "string",
            int => "integer",
            long => "integer64",
            decimal => "decimal",
            double => "decimal",
            bool => "boolean",
            _ => "object"
        };

        return new PrimitiveElement(value, typeName);
    }

    private string FormatLogResult(IEnumerable<IElement> result)
    {
        var elements = result.ToList();
        if (!elements.Any())
        {
            return "(empty)";
        }

        if (elements.Count == 1)
        {
            var element = elements[0];
            if (element.Value != null)
            {
                return element.Value.ToString() ?? "(null)";
            }
            return $"{element.InstanceType}: {element.Name}";
        }

        // Multiple elements - format as comma-separated list
        return string.Join(", ", elements.Select(e =>
            e.Value != null ? e.Value.ToString() : $"{e.InstanceType}: {e.Name}"));
    }

    /// <summary>
    /// Simple implementation of IElement for primitive values produced by the mapping evaluator.
    /// </summary>
    /// <remarks>Declares <see cref="ISystemValueElement"/>; see that interface for why.</remarks>
    private class PrimitiveElement : ISystemValueElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Wrapper element that provides access to mapping context variables.
    /// Allows FhirPath expressions to resolve mapping variables like "src" and "tgt".
    /// </summary>
    private class MappingContextElement : IElement
    {
        private readonly MappingContext _context;
        private readonly Func<IElement, IElement> _refresh;

        public MappingContextElement(MappingContext context, Func<IElement, IElement> refresh)
        {
            _context = context;
            _refresh = refresh;
            Focus = context.Focus;
        }

        public IElement? Focus { get; }
        public string Name => Focus?.Name ?? string.Empty;
        public string InstanceType => Focus?.InstanceType ?? "MappingContext";
        public object? Value => Focus?.Value;
        public string Location => string.Empty;
        public IType? Type => Focus?.Type;
        public bool HasPrimitiveValue => Focus?.HasPrimitiveValue ?? false;

        public IReadOnlyList<IElement> Children(string? name)
        {
            List<IElement> result = [];

            if (name == null)
            {
                // Return all sources and targets as children
                result.AddRange(Focus is { } focus ? _refresh(focus).Children() : []);
                result.AddRange(_context.Sources.Select(s => _refresh(s.Value)));
                result.AddRange(_context.Targets.Select(t => _refresh(t.Value)));
            }
            else
            {
                // Try to resolve as a source variable
                var source = _context.GetSource(name);
                if (source != null)
                {
                    result.Add(_refresh(source));
                    return result;
                }

                // Try to resolve as a target variable
                var target = _context.GetTarget(name);
                if (target != null)
                {
                    result.Add(_refresh(target));
                    return result;
                }

                // Try to resolve as a general variable
                if (_context.ResolveElements(name) is { } values)
                {
                    result.AddRange(values.Select(_refresh));
                }
                else if (Focus is { } focus)
                {
                    result.AddRange(_refresh(focus).Children(name));
                }
            }

            return result;
        }

        public T? Meta<T>() where T : class => null;

    }

    /// <summary>
    /// Preserves a System-valued focus when mapping variables are exposed alongside it.
    /// </summary>
    private sealed class SystemMappingContextElement(MappingContext context, Func<IElement, IElement> refresh)
        : MappingContextElement(context, refresh), ISystemValueElement;

    /// <summary>
    /// Builds a string representation of an expression for matching against log expressions.
    /// </summary>
    private string BuildExpressionString(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression id => id.Name,
            QualifiedIdentifierExpression qual => $"{BuildExpressionString(qual.Context)}.{qual.Property}",
            IndexExpression idx => $"{BuildExpressionString(idx.Context)}[{idx.Index}]",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Checks if the error collection limit has been exceeded.
    /// </summary>
    private void CheckErrorLimit(MappingContext context)
    {
        if (context.Errors.Count >= _options.MaxErrorsCollected)
        {
            throw new MappingExecutionException(
                $"Maximum errors collected ({_options.MaxErrorsCollected}). " +
                $"Stopping evaluation to prevent memory exhaustion.",
                null,
                "MAX_ERRORS_EXCEEDED");
        }
    }

    /// <summary>
    /// Tracks element creation and checks if limit has been exceeded.
    /// </summary>
    private void TrackElementCreation()
    {
        _elementsCreated++;
        if (_elementsCreated > _options.MaxElementsCreated)
        {
            throw new MappingExecutionException(
                $"Maximum elements created exceeded ({_options.MaxElementsCreated}). " +
                $"Transformation may be creating too many elements.",
                null,
                "MAX_ELEMENTS_EXCEEDED");
        }
    }

    /// <summary>
    /// Mutates the target resource by setting the property specified in the target expression.
    /// Extracts the ResourceJsonNode from context and calls IJsonNodeMutator.
    /// </summary>
    private void MutateTargetResource(TargetExpression target, IElement element, MappingContext context, string? location)
    {
        try
        {
            // Extract root context and property path from target.Context
            // For "tgt.id", target.Context is QualifiedIdentifierExpression(tgt, id)
            // For "tgt", target.Context is IdentifierExpression(tgt)
            string contextName;
            string propertyPath;

            if (target.Context is QualifiedIdentifierExpression qualifiedExpr)
            {
                // Extract root context (e.g., "tgt" from "tgt.id.family")
                contextName = ExtractRootContext(qualifiedExpr);
                // Extract property path (e.g., "id.family" from "tgt.id.family")
                propertyPath = ExtractPropertyPath(qualifiedExpr);
            }
            else if (target.Context is IdentifierExpression identifierExpr)
            {
                // Simple context without property (e.g., "tgt")
                // This shouldn't happen for mutation, but handle it gracefully
                contextName = identifierExpr.Name;

                // If there's a variable, use it as the property path
                if (target.Variable != null)
                {
                    propertyPath = target.Variable;
                }
                else
                {
                    // No property path - can't mutate
                    return;
                }
            }
            else
            {
                // Unsupported context expression type
                return;
            }

            // Get target ResourceJsonNode from context
            var targetResource = context.GetTargetResource<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(contextName);
            if (targetResource == null)
            {
                if (context.ErrorMode == ErrorMode.Strict)
                {
                    throw new MappingExecutionException(
                        $"Target resource '{contextName}' not found or is not a ResourceJsonNode",
                        location,
                        "TARGET_RESOURCE_NOT_FOUND");
                }
                return;
            }

            // Build FHIRPath from resource type and property path
            // e.g., "Patient.id" or "Patient.name.family"
            var fhirPath = $"{targetResource.ResourceType}.{propertyPath}";

            // Determine mutation mode from list mode
            var mutationMode = GetMutationMode(target.ListMode);
            var targetNode = targetResource.ToSourceNavigator().Meta<JsonNode>()
                ?? throw new InvalidOperationException($"Target resource '{contextName}' is not JSON-backed.");
            _targetRoots.Add(JsonRoot(targetNode));

            // Apply mutation
            _mutator!.SetProperty(targetResource, fhirPath, element, mutationMode);
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
        {
            context.AddError(
                $"Failed to mutate target property: {ex.Message}",
                location,
                "TARGET_MUTATION_ERROR",
                ex,
                ruleName: _currentRuleName,
                groupName: _currentGroupName,
                ruleIndex: _currentRuleIndex);
        }
    }

    /// <summary>
    /// Extracts the root context name from a QualifiedIdentifierExpression.
    /// For "tgt.id.family", returns "tgt".
    /// </summary>
    private static string ExtractRootContext(QualifiedIdentifierExpression expr)
    {
        // Walk down the Context chain until we find an IdentifierExpression
        Expression current = expr;
        while (current is QualifiedIdentifierExpression qualified)
        {
            current = qualified.Context;
        }

        if (current is IdentifierExpression identifier)
        {
            return identifier.Name;
        }

        throw new InvalidOperationException($"Unable to extract root context from {expr}");
    }

    /// <summary>
    /// Extracts the property path from a QualifiedIdentifierExpression.
    /// For "tgt.id.family", returns "id.family".
    /// For "tgt.name", returns "name".
    /// </summary>
    private static string ExtractPropertyPath(QualifiedIdentifierExpression expr)
    {
        List<string> parts = [];

        // Build the parts list by walking down the expression tree
        Expression current = expr;
        while (current is QualifiedIdentifierExpression qualified)
        {
            parts.Insert(0, qualified.Property);
            current = qualified.Context;
        }

        return string.Join('.', parts);
    }

    /// <summary>
    /// Maps FML list mode to PropertyMutationMode.
    /// </summary>
    private static PropertyMutationMode GetMutationMode(ListMode? listMode)
    {
        return listMode switch
        {
            ListMode.OnlyOne => PropertyMutationMode.Replace,
            ListMode.Share => PropertyMutationMode.Replace,
            ListMode.Single => PropertyMutationMode.Replace,
            ListMode.First or ListMode.Last or ListMode.NotFirst or ListMode.NotLast => PropertyMutationMode.Append,
            _ => PropertyMutationMode.AutoDetect
        };
    }

    /// <summary>
    /// Temporary wrapper that adds a property with specific values to an element.
    /// Used for check conditions with default values.
    /// </summary>
    private class TempPropertyWrapper : IElement
    {
        private readonly IElement _wrapped;
        private readonly string _propertyName;
        private readonly IReadOnlyList<IElement> _propertyValues;

        public TempPropertyWrapper(IElement wrapped, string propertyName, IReadOnlyList<IElement> propertyValues)
        {
            _wrapped = wrapped;
            _propertyName = propertyName;
            _propertyValues = propertyValues;
        }

        public string Name => _wrapped.Name ?? string.Empty;
        public string InstanceType => _wrapped.InstanceType;
        public object? Value => _wrapped.Value;
        public string Location => _wrapped.Location ?? string.Empty;
        public IType? Type => _wrapped.Type;
        public bool HasPrimitiveValue => _wrapped.HasPrimitiveValue;

        public IReadOnlyList<IElement> Children(string? name)
        {
            if (name == _propertyName)
            {
                // Return our injected property values
                return _propertyValues;
            }

            // Delegate to wrapped element for other properties
            return _wrapped.Children(name);
        }

        public T? Meta<T>() where T : class => _wrapped.Meta<T>();
    }
}
