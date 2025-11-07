/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * FHIR Mapping Language evaluator.
 * Executes parsed mapping AST to transform FHIR resources.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Evaluates FHIR Mapping Language expressions to transform resources.
/// Uses the visitor pattern to traverse the expression tree.
/// </summary>
public class MappingEvaluator
{
    private readonly FhirPathIntegration? _fhirPathIntegration;
    private readonly ImportResolver? _importResolver;

    /// <summary>
    /// Creates a new MappingEvaluator instance.
    /// </summary>
    /// <param name="enableFhirPath">Whether to enable FhirPath integration</param>
    /// <param name="importResolver">Optional import resolver for cross-map group invocation</param>
    public MappingEvaluator(bool enableFhirPath = true, ImportResolver? importResolver = null)
    {
        _fhirPathIntegration = enableFhirPath ? new FhirPathIntegration() : null;
        _importResolver = importResolver;
    }
    /// <summary>
    /// Executes a map expression to transform source resources to target resources.
    /// </summary>
    /// <param name="map">The parsed map expression</param>
    /// <param name="context">The evaluation context with sources and targets</param>
    public void Execute(MapExpression map, MappingContext context)
    {
        // Wire up standard transforms if not already configured
        context.TransformResolver ??= (name, args) => StandardTransforms.Get(name)!.Execute(args.ToList(), context);

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
        // Wire up standard transforms if not already configured
        context.TransformResolver ??= (name, args) => StandardTransforms.Get(name)!.Execute(args.ToList(), context);

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

        try
        {
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
                // Try to find base group in current map first
                var baseGroup = map.Groups.FirstOrDefault(g => g.Name == group.Extends);

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
            foreach (var rule in group.Rules)
            {
                VisitRule(rule, context, group.Name);
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error executing group: {ex.Message}", location, "GROUP_EXECUTION_ERROR", ex);
        }
    }

    private void VisitRule(RuleExpression rule, MappingContext context, string? groupName = null)
    {
        var ruleName = !string.IsNullOrEmpty(rule.Name) ? rule.Name : "anonymous";
        var location = groupName != null ? $"Group: {groupName}, Rule: {ruleName}" : $"Rule: {ruleName}";

        try
        {
            // Visit sources
            var sourceValues = new Dictionary<string, IEnumerable<ITypedElement>>();
            foreach (var source in rule.Sources)
            {
                try
                {
                    var values = VisitSource(source, context, location);
                    if (source.Variable != null)
                    {
                        sourceValues[source.Variable] = values;
                    }
                }
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                {
                    context.AddError($"Error evaluating source: {ex.Message}", location, "SOURCE_ERROR", ex);
                    // Continue with other sources
                    if (source.Variable != null)
                    {
                        sourceValues[source.Variable] = Enumerable.Empty<ITypedElement>();
                    }
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
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                {
                    context.AddError($"Error evaluating target: {ex.Message}", location, "TARGET_ERROR", ex);
                    // Continue with other targets
                }
            }

            // Visit dependent rules
            foreach (var dependentRule in rule.Dependent)
            {
                VisitRule(dependentRule, context, groupName);
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error executing rule: {ex.Message}", location, "RULE_EXECUTION_ERROR", ex);
        }
    }

    private IEnumerable<ITypedElement> VisitSource(SourceExpression source, MappingContext context, string? location = null)
    {
        try
        {
            // Visit the source context expression
            var contextValues = VisitExpression(source.Context, context, location);

            // Apply default value if source is empty and default is specified
            if (!contextValues.Any() && source.Default != null)
            {
                // Evaluate the default expression
                contextValues = VisitExpression(source.Default, context, location);
            }

            // Apply where condition if present
            if (source.Condition != null && source.Condition is FhirPathExpression fhirPathCondition)
            {
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
                    catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                    {
                        context.AddError($"Error evaluating where condition: {ex.Message}", location, "WHERE_ERROR", ex);
                        return false; // Exclude element on error
                    }
                });
            }

            // Check cardinality constraint if present
            if (source.Cardinality != null)
            {
                var contextList = contextValues.ToList();
                var count = contextList.Count;

                if (!source.Cardinality.IsSatisfiedBy(count))
                {
                    var message = $"Cardinality constraint {source.Cardinality} not satisfied: found {count} element(s)";
                    context.AddError(message, location, "CARDINALITY_ERROR");
                }

                // Use the materialized list for further processing
                contextValues = contextList;
            }

            // Materialize context values to avoid multiple enumeration
            var contextValuesList = contextValues.ToList();

            // Apply check condition if present
            if (source.Check != null && source.Check is FhirPathExpression fhirPathCheck)
            {
                foreach (var element in contextValuesList)
                {
                    try
                    {
                        if (context.FhirPathEvaluator == null)
                        {
                            throw new InvalidOperationException("FhirPathEvaluator not configured in context");
                        }

                        var result = context.FhirPathEvaluator(fhirPathCheck.PathExpression, element);
                        if (!result.Any() || result.First().Value is not bool b || !b)
                        {
                            throw new InvalidOperationException($"Check condition failed: {fhirPathCheck.PathExpression}");
                        }
                    }
                    catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                    {
                        context.AddError($"Check condition failed: {ex.Message}", location, "CHECK_ERROR", ex);
                        // Continue processing other elements
                    }
                }
            }

            // Execute log statement if present (only if we have values to log)
            if (source.Log != null && contextValuesList.Any())
            {
                try
                {
                    // Check if the log expression matches the source context expression
                    // If so, just log the contextValues directly (which may include defaults)
                    bool logMatchesContext = source.Log is FhirPathExpression fhirPathLog &&
                                              source.Context is QualifiedIdentifierExpression qual &&
                                              fhirPathLog.PathExpression == $"{((IdentifierExpression)qual.Context).Name}.{qual.Property}";

                    if (!logMatchesContext && source.Context is IdentifierExpression id &&
                        source.Log is FhirPathExpression fp && fp.PathExpression == id.Name)
                    {
                        logMatchesContext = true;
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
                            IEnumerable<ITypedElement> logResult;

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
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                {
                    context.AddError($"Error executing log statement: {ex.Message}", location, "LOG_ERROR", ex);
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
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error visiting source: {ex.Message}", location, "SOURCE_VISIT_ERROR", ex);
            return Enumerable.Empty<ITypedElement>();
        }
    }

    private void VisitTarget(TargetExpression target, MappingContext context, Dictionary<string, IEnumerable<ITypedElement>> sourceValues, string? location = null)
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
                    // If there's a transform, visit it
                    if (target.Transform != null)
                    {
                        var transformResult = VisitTransform(target.Transform, context, location);

                        // Set the result to the target context if specified
                        if (target.Context != null && transformResult is ITypedElement element)
                        {
                            if (target.Variable != null)
                            {
                                context.SetVariable(target.Variable, element);
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
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                {
                    context.AddError($"Error processing target element: {ex.Message}", location, "TARGET_ELEMENT_ERROR", ex);
                    // Continue with next element
                }
            }
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error visiting target: {ex.Message}", location, "TARGET_VISIT_ERROR", ex);
        }
    }

    private IEnumerable<ITypedElement> ApplyListModeFiltering(IReadOnlyList<ITypedElement> elements, ListMode? listMode)
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

    private IEnumerable<ITypedElement> ValidateOnlyOne(IReadOnlyList<ITypedElement> elements)
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
            var args = new List<object>();
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
                catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
                {
                    context.AddError($"Error evaluating transform argument: {ex.Message}", location, "TRANSFORM_ARG_ERROR", ex);
                    // Continue with other arguments
                }
            }

            // Call the transform function
            return context.TransformResolver(transform.FunctionName, args);
        }
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error executing transform '{transform.FunctionName}': {ex.Message}", location, "TRANSFORM_ERROR", ex);
            return null;
        }
    }

    private IEnumerable<ITypedElement> VisitExpression(Expression expr, MappingContext context, string? location = null)
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
        catch (Exception ex) when (context.ErrorMode == ErrorMode.Graceful)
        {
            context.AddError($"Error evaluating expression: {ex.Message}", location, "EXPRESSION_ERROR", ex);
            return Enumerable.Empty<ITypedElement>();
        }
    }

    private IEnumerable<ITypedElement> VisitIdentifier(IdentifierExpression id, MappingContext context)
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
        if (variable is ITypedElement element)
        {
            return new[] { element };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> VisitQualifiedIdentifier(QualifiedIdentifierExpression qual, MappingContext context)
    {
        // Visit the context first
        var contextElements = VisitExpression(qual.Context, context);

        // Navigate to the property
        foreach (var element in contextElements)
        {
            foreach (var child in element.Children(qual.Property))
            {
                yield return child;
            }
        }
    }

    private IEnumerable<ITypedElement> VisitIndex(IndexExpression idx, MappingContext context)
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

    private IEnumerable<ITypedElement> VisitFhirPath(FhirPathExpression fhirPath, MappingContext context)
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

        // Fall back to FHIRPath evaluation for pure FHIRPath expressions (e.g., "'hello'", "0", "name.first()")
        // Use an empty root element so that literals and environment variables work
        var emptyRoot = new PrimitiveElement(null!, "Element");
        return context.FhirPathEvaluator(fhirPath.PathExpression, emptyRoot);
    }

    private bool TryParseAsMappingExpression(string expression, MappingContext context, out IEnumerable<ITypedElement> result)
    {
        result = Enumerable.Empty<ITypedElement>();

        // Check for simple identifier or qualified identifier patterns
        // Simple identifier: "src", "tgt"
        // Qualified identifier: "src.gender", "src.name.given"
        // Indexed: "src.name[0]"

        var parts = new List<string>();
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
            if (variable is ITypedElement element)
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
                        current = index >= 0 && index < list.Count ? new[] { list[index] } : Enumerable.Empty<ITypedElement>();
                    }
                }
                else
                {
                    // Navigate to property first, then index
                    current = current.SelectMany(e => e.Children(propertyName));

                    if (int.TryParse(indexStr, out var index))
                    {
                        var list = current.ToList();
                        current = index >= 0 && index < list.Count ? new[] { list[index] } : Enumerable.Empty<ITypedElement>();
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

    private ITypedElement CreatePrimitive(object value)
    {
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

    private string FormatLogResult(IEnumerable<ITypedElement> result)
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
    /// Simple implementation of ITypedElement for primitive values.
    /// </summary>
    private class PrimitiveElement : ITypedElement
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
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }
}
