/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language evaluator.
 * Executes parsed mapping AST to transform FHIR resources.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Abstractions;

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
    private MapExpression? _currentMap;

    // Execution context tracking for enhanced error messages
    private string? _currentGroupName;
    private int? _currentRuleIndex;
    private string? _currentRuleName;

    // Security limit tracking
    private int _recursionDepth;
    private int _elementsCreated;

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
    }

    /// <summary>
    /// Executes a map expression to transform source resources to target resources.
    /// </summary>
    /// <param name="map">The parsed map expression</param>
    /// <param name="context">The evaluation context with sources and targets</param>
    public void Execute(MapExpression map, MappingContext context)
    {
        _currentMap = map;

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
            context.FhirPathEvaluator = (expression, element) => _fhirPathIntegration.Evaluate(expression, element);
        }

        // Execute each group in the map
        foreach (var group in map.Groups)
        {
            VisitGroup(group, map, context);
        }
    }

    /// <summary>
    /// Executes a specific group by name with provided arguments.
    /// </summary>
    public void ExecuteGroup(MapExpression map, string groupName, MappingContext context)
    {
        _currentMap = map;

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
            context.FhirPathEvaluator = (expression, element) => _fhirPathIntegration.Evaluate(expression, element);
        }

        var group = map.Groups.FirstOrDefault(g => g.Name == groupName);
        if (group == null)
        {
            throw new InvalidOperationException($"Group '{groupName}' not found in map");
        }

        VisitGroup(group, map, context);
    }

    private void VisitGroup(GroupExpression group, MapExpression map, MappingContext context, HashSet<string>? visitedGroups = null)
    {
        var location = $"Group: {group.Name}";
        var previousGroupName = _currentGroupName;
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
            visitedGroups ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Check for circular inheritance
            if (!visitedGroups.Add(group.Name))
            {
                throw new InvalidOperationException(
                    $"Circular group inheritance detected: group '{group.Name}' is part of an inheritance cycle");
            }

            // Handle group inheritance (extends)
            if (!string.IsNullOrEmpty(group.Extends))
            {
                // Try to find base group in current map first (case-insensitive)
                var baseGroup = map.Groups.FirstOrDefault(g => string.Equals(g.Name, group.Extends, StringComparison.OrdinalIgnoreCase));

                // If not found and import resolver is available, check imports
                if (baseGroup == null && _importResolver != null)
                {
                    baseGroup = _importResolver.FindGroup(map, group.Extends);
                }

                if (baseGroup == null)
                {
                    throw new InvalidOperationException(
                        $"Group '{group.Name}' extends '{group.Extends}', but base group not found");
                }

                // Execute base group first (recursive, handles transitive inheritance)
                VisitGroup(baseGroup, map, context, visitedGroups);
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

            // Visit sources
            var sourceValues = new Dictionary<string, IEnumerable<IElement>>();
            var anonymousSourceIndex = 0;
            foreach (var source in rule.Sources)
            {
                try
                {
                    var values = VisitSource(source, context, location);

                    // Store source values with either the variable name or an anonymous key
                    var key = source.Variable ?? $"__anonymous_{anonymousSourceIndex++}";
                    sourceValues[key] = values;
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    CheckErrorLimit(context);
                    context.AddError(
                        $"Error evaluating source: {ex.Message}",
                        location,
                        "SOURCE_ERROR",
                        ex,
                        ruleName: ruleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                    // Continue with other sources
                    var key = source.Variable ?? $"__anonymous_{anonymousSourceIndex++}";
                    sourceValues[key] = [];
                }
            }

            // If any source has no values and there's no condition allowing empty, skip this rule
            if (sourceValues.Any(kvp => !kvp.Value.Any()))
            {
                return;
            }

            // Visit targets with list mode filtering
            foreach (var target in rule.Targets)
            {
                try
                {
                    VisitTarget(target, context, sourceValues, location);
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    CheckErrorLimit(context);
                    context.AddError(
                        $"Error evaluating target: {ex.Message}",
                        location,
                        "TARGET_ERROR",
                        ex,
                        ruleName: ruleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                    // Continue with other targets
                }
            }

            // Visit dependent expression (either group invocation or nested rules)
            if (rule.Dependent != null)
            {
                VisitDependentExpression(rule.Dependent, context, groupName, location);
            }
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

    private void VisitDependentExpression(Expression dependent, MappingContext context, string? groupName, string? location)
    {
        try
        {
            switch (dependent)
            {
                case RuleSetExpression ruleSet:
                    // Execute nested rules
                    foreach (var nestedRule in ruleSet.Rules)
                    {
                        VisitRule(nestedRule, context, groupName);
                    }
                    break;

                case GroupInvocationExpression groupInvocation:
                    // Execute group invocation
                    VisitGroupInvocation(groupInvocation, context, location);
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

    private void VisitGroupInvocation(GroupInvocationExpression invocation, MappingContext context, string? location)
    {
        try
        {
            if (_currentMap == null)
            {
                throw new InvalidOperationException("Cannot invoke group - no map context available");
            }

            // Find the group to invoke (case-insensitive)
            var targetGroup = _currentMap.Groups.FirstOrDefault(g =>
                string.Equals(g.Name, invocation.GroupName, StringComparison.OrdinalIgnoreCase));

            if (targetGroup == null && _importResolver != null)
            {
                // Try to find in imported maps
                targetGroup = _importResolver.FindGroup(_currentMap, invocation.GroupName);
            }

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

            try
            {
                // Save current parameter values and set new ones from arguments
                for (int i = 0; i < targetGroup.Parameters.Count; i++)
                {
                    var param = targetGroup.Parameters[i];
                    var arg = invocation.Arguments[i];

                    // Evaluate the argument
                    var argValue = VisitExpression(arg, context, location).FirstOrDefault();

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
                // Restore original parameter values
                foreach (var kvp in originalSources)
                {
                    if (kvp.Value != null)
                    {
                        context.SetSource(kvp.Key, kvp.Value);
                    }
                }

                foreach (var kvp in originalTargets)
                {
                    if (kvp.Value != null)
                    {
                        context.SetTarget(kvp.Key, kvp.Value);
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

            // Apply default value if source is empty and default is specified
            if (!contextValues.Any() && source.Default != null)
            {
                // Evaluate the default expression
                contextValues = VisitExpression(source.Default, context, location).ToList();
            }

            // Apply where condition if present
            if (source.Condition != null && source.Condition is FhirPathExpression fhirPathCondition)
            {
                // Check if the where condition references mapping variables (contains function calls or mapping variable names)
                var whereExpr = fhirPathCondition.PathExpression.Trim();
                bool isContextualWhere = whereExpr.Contains('(', StringComparison.Ordinal) ||
                                        whereExpr.StartsWith("src.", StringComparison.Ordinal) ||
                                        whereExpr.StartsWith("tgt.", StringComparison.Ordinal);

                if (isContextualWhere)
                {
                    // Evaluate once against mapping context
                    try
                    {
                        var whereResult = VisitFhirPath(fhirPathCondition, context);
                        bool conditionMet = whereResult.Any() && whereResult.First().Value is bool b && b;

                        if (!conditionMet)
                        {
                            // Filter out all elements
                            contextValues = [];
                        }
                        // Otherwise keep all elements
                    }
                    catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                    {
                        context.AddError(
                            $"Error evaluating where condition: {ex.Message}",
                            location,
                            "WHERE_ERROR",
                            ex,
                            ruleName: _currentRuleName,
                            groupName: _currentGroupName,
                            ruleIndex: _currentRuleIndex);
                        contextValues = []; // Exclude all on error
                    }
                }
                else
                {
                    // Evaluate against each element (for element-specific conditions like "use='official'")
                    contextValues = contextValues.Where(element =>
                    {
                        try
                        {
                            if (context.FhirPathEvaluator == null)
                            {
                                throw new InvalidOperationException("FhirPathEvaluator not configured in context");
                            }

                            var result = context.FhirPathEvaluator(fhirPathCondition.PathExpression, element);
                            return result.Any() && result.First().Value is bool b && b;
                        }
                        catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                        {
                            context.AddError(
                                $"Error evaluating where condition: {ex.Message}",
                                location,
                                "WHERE_ERROR",
                                ex,
                                ruleName: _currentRuleName,
                                groupName: _currentGroupName,
                                ruleIndex: _currentRuleIndex);
                            return false; // Exclude element on error
                        }
                    });
                }
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
            var contextValuesList = contextValues.ToList();

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

                    // If we have context values (including from defaults), create a wrapper that provides access to them
                    // This allows check expressions like "src.active = true" to work even when active came from a default
                    MappingContext checkContext = context;
                    IElement? tempWrapper = null;

                    if (contextValuesList.Any() && source.Context is QualifiedIdentifierExpression qual)
                    {
                        // Extract the root and property from the qualified identifier
                        var rootExpr = qual.Context;
                        var propertyName = qual.Property;

                        if (rootExpr is IdentifierExpression rootId)
                        {
                            var originalRoot = context.GetSource(rootId.Name) ?? context.GetTarget(rootId.Name);
                            if (originalRoot != null)
                            {
                                // Create a wrapper that adds the context values as a child property
                                tempWrapper = new TempPropertyWrapper(originalRoot, propertyName, contextValuesList as IReadOnlyList<IElement> ?? contextValuesList.ToList());
                                checkContext = new MappingContext();
                                checkContext.SetSource(rootId.Name, tempWrapper);

                                // Copy other sources/targets
                                foreach (var (name, element) in GetAllContextElements(context))
                                {
                                    if (name != rootId.Name)
                                    {
                                        checkContext.SetSource(name, element);
                                    }
                                }

                                checkContext.FhirPathEvaluator = context.FhirPathEvaluator;
                            }
                        }
                    }

                    // Evaluate the check expression in the mapping context
                    var checkResult = VisitFhirPath(fhirPathCheck, checkContext);
                    if (!checkResult.Any() || checkResult.First().Value is not bool b || !b)
                    {
                        throw new InvalidOperationException($"Check condition failed: {fhirPathCheck.PathExpression}");
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
                                logResult = VisitFhirPath(logExpr, context);
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

    private void VisitTarget(TargetExpression target, MappingContext context, Dictionary<string, IEnumerable<IElement>> sourceValues, string? location = null)
    {
        try
        {
            // Determine the collection to iterate over (typically comes from source values)
            // For now, we use all source values combined as the basis for list mode filtering
            var allSourceElements = sourceValues.Values.SelectMany(v => v).ToList();

            // Apply list mode filtering
            var filteredElements = ApplyListModeFiltering(allSourceElements, target.ListMode);

            // Process each filtered element
            foreach (var sourceElement in filteredElements)
            {
                try
                {
                    // If there's a transform expression, visit it
                    if (target.Transform is TransformExpression transform)
                    {
                        var transformResult = VisitTransform(transform, context, location);

                        // Set the result to the target context if specified
                        if (target.Context != null && transformResult is IElement element)
                        {
                            if (target.Variable != null)
                            {
                                context.SetVariable(target.Variable, element);
                            }
                        }
                    }
                    else if (target.Transform != null)
                    {
                        // Handle literal value assignment (e.g., tgt.type = 'collection')
                        var expressionResult = VisitExpression(target.Transform, context, location);
                        if (target.Context != null && expressionResult.Any())
                        {
                            if (target.Variable != null)
                            {
                                context.SetVariable(target.Variable, expressionResult.First());
                            }
                        }
                    }
                    else if (target.Context != null)
                    {
                        // Simple assignment without transform
                        var contextValues = VisitExpression(target.Context, context, location);
                        if (target.Variable != null)
                        {
                            context.SetVariable(target.Variable, contextValues.FirstOrDefault()!);
                        }
                    }
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
                {
                    context.AddError(
                        $"Error processing target element: {ex.Message}",
                        location,
                        "TARGET_ELEMENT_ERROR",
                        ex,
                        ruleName: _currentRuleName,
                        groupName: _currentGroupName,
                        ruleIndex: _currentRuleIndex);
                    // Continue with next element
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

    private object? VisitTransform(TransformExpression transform, MappingContext context, string? location = null)
    {
        try
        {
            if (context.TransformResolver == null)
            {
                throw new InvalidOperationException("TransformResolver not configured in context");
            }

            // Visit arguments
            List<object> args = [];
            foreach (var arg in transform.Arguments)
            {
                try
                {
                    var argValue = VisitExpression(arg, context, location).FirstOrDefault();
                    if (argValue != null)
                    {
                        args.Add(argValue.Value ?? argValue);
                    }
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

            // Call the transform function
            return context.TransformResolver(transform.FunctionName, args);
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
            return new[] { source };
        }

        // Check if it's a target
        var target = context.GetTarget(id.Name);
        if (target != null)
        {
            return new[] { target };
        }

        // Check if it's a variable
        var variable = context.GetVariable(id.Name);
        if (variable is IElement element)
        {
            return new[] { element };
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
            var children = element.Children(qual.Property).ToList();
            result.AddRange(children);
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

        // Try to parse and evaluate as a Mapping Language expression first
        // This handles cases like "src.gender" which refer to mapping variables
        var pathExpr = fhirPath.PathExpression.Trim();

        // Try to parse as a mapping language qualified identifier (e.g., "src.gender")
        if (TryParseAsMappingExpression(pathExpr, context, out var mappingResult))
        {
            return mappingResult;
        }

        // Fall back to FHIRPath evaluation for FHIRPath expressions with function calls
        // (e.g., "src.id.exists()", "src.name.count()>0", "'hello'", "0")
        // Create a context root element that provides access to mapping variables
        var contextRoot = new MappingContextElement(context);
        return context.FhirPathEvaluator(fhirPath.PathExpression, contextRoot);
    }

    private bool TryParseAsMappingExpression(string expression, MappingContext context, out IEnumerable<IElement> result)
    {
        result = Enumerable.Empty<IElement>();

        // Check for simple identifier or qualified identifier patterns
        // Simple identifier: "src", "tgt"
        // Qualified identifier: "src.gender", "src.name.given"
        // Indexed: "src.name[0]"

        // If expression contains function calls (parentheses), operators, or comparisons,
        // it's a FhirPath expression and should not be parsed as a mapping expression
        if (expression.Contains('(', StringComparison.Ordinal) || expression.Contains('>', StringComparison.Ordinal) || expression.Contains('<', StringComparison.Ordinal) ||
            expression.Contains('=', StringComparison.Ordinal) || expression.Contains('+', StringComparison.Ordinal) || expression.Contains('-', StringComparison.Ordinal) ||
            expression.Contains('*', StringComparison.Ordinal) || expression.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        List<string> parts = [];
        var currentPart = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var ch in expression)
        {
            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
            }
            else if (ch == '.' && depth == 0)
            {
                if (currentPart.Length > 0)
                {
                    parts.Add(currentPart.ToString());
                    currentPart.Clear();
                }
                continue;
            }

            currentPart.Append(ch);
        }

        if (currentPart.Length > 0)
        {
            parts.Add(currentPart.ToString());
        }

        if (parts.Count == 0)
        {
            return false;
        }

        // Check if the first part is a known variable
        var rootName = parts[0].Split('[')[0]; // Handle indexed access like "src[0]"
        var rootElement = context.GetSource(rootName) ?? context.GetTarget(rootName);

        if (rootElement == null)
        {
            var variable = context.GetVariable(rootName);
            if (variable is IElement element)
            {
                rootElement = element;
            }
        }

        if (rootElement == null)
        {
            return false; // Not a mapping variable
        }

        // Navigate through the path
        var current = new[] { rootElement }.AsEnumerable();

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            // Handle array indexing
            if (part.Contains('[', StringComparison.Ordinal) && part.Contains(']', StringComparison.Ordinal))
            {
                var propertyName = part.Substring(0, part.IndexOf('[', StringComparison.Ordinal));
                var indexStr = part.Substring(part.IndexOf('[', StringComparison.Ordinal) + 1, part.IndexOf(']', StringComparison.Ordinal) - part.IndexOf('[', StringComparison.Ordinal) - 1);

                if (i == 0)
                {
                    // Indexing on the root variable (e.g., "src[0]")
                    if (int.TryParse(indexStr, out var index))
                    {
                        var list = current.ToList();
                        current = index >= 0 && index < list.Count ? new[] { list[index] } : [];
                    }
                }
                else
                {
                    // Navigate to property first, then index
                    current = current.SelectMany(e => e.Children(propertyName));

                    if (int.TryParse(indexStr, out var index))
                    {
                        var list = current.ToList();
                        current = index >= 0 && index < list.Count ? new[] { list[index] } : [];
                    }
                }
            }
            else if (i > 0) // Skip the root, we already have it
            {
                // Navigate to property
                current = current.SelectMany(e => e.Children(part));
            }
        }

        result = current;
        return true;
    }

    private IElement CreatePrimitive(object value)
    {
        TrackElementCreation();

        var typeName = value switch
        {
            string => "string",
            int => "integer",
            decimal => "decimal",
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
    /// Simple implementation of IElement for primitive values.
    /// </summary>
    private class PrimitiveElement : IElement
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

        public MappingContextElement(MappingContext context)
        {
            _context = context;
        }

        public string Name => string.Empty;
        public string InstanceType => "MappingContext";
        public object? Value => null;
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name)
        {
            List<IElement> result = [];

            if (name == null)
            {
                // Return all sources and targets as children
                result.AddRange(GetAllSources());
                result.AddRange(GetAllTargets());
            }
            else
            {
                // Try to resolve as a source variable
                var source = _context.GetSource(name);
                if (source != null)
                {
                    result.Add(source);
                    return result;
                }

                // Try to resolve as a target variable
                var target = _context.GetTarget(name);
                if (target != null)
                {
                    result.Add(target);
                    return result;
                }

                // Try to resolve as a general variable
                var variable = _context.GetVariable(name);
                if (variable is IElement element)
                {
                    result.Add(element);
                }
            }

            return result;
        }

        public T? Meta<T>() where T : class => null;

        private IEnumerable<IElement> GetAllSources()
        {
            // Use reflection to access the private _sources dictionary
            var contextType = _context.GetType();
            var sourcesField = contextType.GetField("_sources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (sourcesField?.GetValue(_context) is Dictionary<string, IElement> sources)
            {
                return sources.Values;
            }
            return Enumerable.Empty<IElement>();
        }

        private IEnumerable<IElement> GetAllTargets()
        {
            // Use reflection to access the private _targets dictionary
            var contextType = _context.GetType();
            var targetsField = contextType.GetField("_targets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (targetsField?.GetValue(_context) is Dictionary<string, IElement> targets)
            {
                return targets.Values;
            }
            return Enumerable.Empty<IElement>();
        }
    }

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
    /// Gets all context elements (sources and targets) from the mapping context.
    /// </summary>
    private IEnumerable<(string name, IElement element)> GetAllContextElements(MappingContext context)
    {
        // Use reflection to access private fields
        var contextType = context.GetType();
        var sourcesField = contextType.GetField("_sources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var targetsField = contextType.GetField("_targets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (sourcesField?.GetValue(context) is Dictionary<string, IElement> sources)
        {
            foreach (var kvp in sources)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }

        if (targetsField?.GetValue(context) is Dictionary<string, IElement> targets)
        {
            foreach (var kvp in targets)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }
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
