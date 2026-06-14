/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Public API for evaluating SQL on FHIR v2 ViewDefinitions.
 * Uses ISourceNavigator for proper handling of FHIR data and visitor pattern for evaluation.
 */

using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Expressions;
using Ignixa.SqlOnFhir.Parsing;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNavigator migration pending

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Evaluates SQL on FHIR v2 ViewDefinitions against FHIR resources.
/// Uses ISourceNavigator-based parsing and visitor pattern for clean, extensible architecture.
/// </summary>
public class SqlOnFhirEvaluator
{
    private readonly SqlOnFhirEvaluationVisitor _visitor;
    private readonly Dictionary<string, ViewDefinitionExpression> _compiledViewDefinitions = [];

    public SqlOnFhirEvaluator()
    {
        _visitor = new SqlOnFhirEvaluationVisitor();
    }

    /// <summary>
    /// Evaluates a ViewDefinition (as ISourceNavigator) against a FHIR resource, returning rows of data.
    /// </summary>
    /// <param name="viewDefinitionNode">The ViewDefinition as ISourceNavigator</param>
    /// <param name="resource">The FHIR resource to evaluate against</param>
    /// <param name="variables">Optional FHIRPath variables to inject into the evaluation context</param>
    /// <returns>List of rows, where each row is a dictionary mapping column names to values</returns>
    public IEnumerable<Dictionary<string, object?>> Evaluate(
        ISourceNavigator viewDefinitionNode,
        IElement resource,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return EvaluateBatch(viewDefinitionNode, [resource], variables);
    }

    /// <summary>
    /// Evaluates a ViewDefinition against multiple FHIR resources with correct UNION ALL ordering.
    /// When a top-level select contains unionAll without forEach, results are ordered by branch
    /// across all resources (SQL UNION ALL semantics) rather than per-resource interleaving.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> EvaluateBatch(
        ISourceNavigator viewDefinitionNode,
        IEnumerable<IElement> resources,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);
        ArgumentNullException.ThrowIfNull(resources);

        var resourceType = viewDefinitionNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";

        try
        {
            var cacheKey = $"{resourceType}_{viewDefinitionNode.GetHashCode()}";

            if (!_compiledViewDefinitions.TryGetValue(cacheKey, out var viewExpr))
            {
                viewExpr = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);
                _compiledViewDefinitions[cacheKey] = viewExpr;
            }

            return _visitor.EvaluateBatch(viewExpr, resources, variables);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to evaluate ViewDefinition for resource type '{resourceType}'",
                ex);
        }
    }

    /// <summary>
    /// Clears the compiled ViewDefinition expression cache.
    /// </summary>
    public void ClearCache()
    {
        _compiledViewDefinitions.Clear();
    }
}
