/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * FHIR Mapping Language evaluator.
 * Executes parsed mapping AST to transform FHIR resources.
 */

using Ignixa.FhirMappingLanguage.Expressions;
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

    /// <summary>
    /// Creates a new MappingEvaluator instance.
    /// </summary>
    /// <param name="enableFhirPath">Whether to enable FhirPath integration</param>
    public MappingEvaluator(bool enableFhirPath = true)
    {
        _fhirPathIntegration = enableFhirPath ? new FhirPathIntegration() : null;
    }
    /// <summary>
    /// Executes a map expression to transform source resources to target resources.
    /// </summary>
    /// <param name="map">The parsed map expression</param>
    /// <param name="context">The evaluation context with sources and targets</param>
    public void Execute(MapExpression map, MappingContext context)
    {
        // Wire up standard transforms if not already configured
        context.TransformResolver ??= (name, args) => StandardTransforms.Get(name).Execute(args.ToList(), context);

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
        context.TransformResolver ??= (name, args) => StandardTransforms.Get(name).Execute(args.ToList(), context);

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
            var baseGroup = map.Groups.FirstOrDefault(g => g.Name == group.Extends);
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
                    throw new InvalidOperationException($"Required source parameter '{param.Name}' not provided");
                }
            }
            else if (param.Mode == ParameterMode.Target)
            {
                if (context.GetTarget(param.Name) == null)
                {
                    throw new InvalidOperationException($"Required target parameter '{param.Name}' not provided");
                }
            }
        }

        // Execute each rule in the group
        foreach (var rule in group.Rules)
        {
            VisitRule(rule, context);
        }
    }

    private void VisitRule(RuleExpression rule, MappingContext context)
    {
        // Visit sources
        var sourceValues = new Dictionary<string, IEnumerable<ITypedElement>>();
        foreach (var source in rule.Sources)
        {
            var values = VisitSource(source, context);
            if (source.Variable != null)
            {
                sourceValues[source.Variable] = values;
            }
        }

        // If any source has no values and there's no condition allowing empty, skip this rule
        if (sourceValues.Any(kvp => !kvp.Value.Any()))
        {
            return;
        }

        // Visit targets
        foreach (var target in rule.Targets)
        {
            VisitTarget(target, context);
        }

        // Visit dependent rules
        foreach (var dependentRule in rule.Dependent)
        {
            VisitRule(dependentRule, context);
        }
    }

    private IEnumerable<ITypedElement> VisitSource(SourceExpression source, MappingContext context)
    {
        // Visit the source context expression
        var contextValues = VisitExpression(source.Context, context);

        // Apply where condition if present
        if (source.Condition != null && source.Condition is FhirPathExpression fhirPathCondition)
        {
            contextValues = contextValues.Where(element =>
            {
                if (context.FhirPathEvaluator == null)
                {
                    throw new InvalidOperationException("FhirPathEvaluator not configured in context");
                }

                var result = context.FhirPathEvaluator(fhirPathCondition.PathExpression, element);
                return result.Any() && result.First().Value is bool b && b;
            });
        }

        // Apply check condition if present
        if (source.Check != null && source.Check is FhirPathExpression fhirPathCheck)
        {
            foreach (var element in contextValues)
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
        }

        // Set variable if specified
        if (source.Variable != null)
        {
            foreach (var element in contextValues)
            {
                context.SetVariable(source.Variable, element);
            }
        }

        return contextValues;
    }

    private void VisitTarget(TargetExpression target, MappingContext context)
    {
        // If there's a transform, visit it
        if (target.Transform != null)
        {
            var transformResult = VisitTransform(target.Transform, context);

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
            var contextValues = VisitExpression(target.Context, context);
            if (target.Variable != null)
            {
                context.SetVariable(target.Variable, contextValues.FirstOrDefault()!);
            }
        }
    }

    private object? VisitTransform(TransformExpression transform, MappingContext context)
    {
        if (context.TransformResolver == null)
        {
            throw new InvalidOperationException("TransformResolver not configured in context");
        }

        // Visit arguments
        var args = new List<object>();
        foreach (var arg in transform.Arguments)
        {
            var argValue = VisitExpression(arg, context).FirstOrDefault();
            if (argValue != null)
            {
                args.Add(argValue.Value ?? argValue);
            }
        }

        // Call the transform function
        return context.TransformResolver(transform.FunctionName, args);
    }

    private IEnumerable<ITypedElement> VisitExpression(Expression expr, MappingContext context)
    {
        return expr switch
        {
            IdentifierExpression id => VisitIdentifier(id, context),
            QualifiedIdentifierExpression qual => VisitQualifiedIdentifier(qual, context),
            LiteralExpression lit => new[] { CreatePrimitive(lit.Value) },
            FhirPathExpression fhirPath => VisitFhirPath(fhirPath, context),
            _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} not supported in this context")
        };
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

    private IEnumerable<ITypedElement> VisitFhirPath(FhirPathExpression fhirPath, MappingContext context)
    {
        if (context.FhirPathEvaluator == null)
        {
            throw new InvalidOperationException("FhirPathEvaluator not configured in context");
        }

        // This is a simplified implementation - in reality, we'd need a root context
        // For now, throw an exception indicating this needs a proper context
        throw new NotImplementedException("FHIRPath evaluation requires a root context element");
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
