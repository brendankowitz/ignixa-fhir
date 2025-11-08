/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Public API for evaluating SQL on FHIR v2 ViewDefinitions.
 * Uses ISourceNode for proper handling of FHIR data and visitor pattern for evaluation.
 */

using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Expressions;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Evaluates SQL on FHIR v2 ViewDefinitions against FHIR resources.
/// Uses ISourceNode-based parsing and visitor pattern for clean, extensible architecture.
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
    /// Evaluates a ViewDefinition (as ISourceNode) against a FHIR resource, returning rows of data.
    /// </summary>
    /// <param name="viewDefinitionNode">The ViewDefinition as ISourceNode</param>
    /// <param name="resource">The FHIR resource to evaluate against</param>
    /// <returns>List of rows, where each row is a dictionary mapping column names to values</returns>
    public IEnumerable<Dictionary<string, object?>> Evaluate(ISourceNode viewDefinitionNode, ITypedElement resource)
    {
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);
        ArgumentNullException.ThrowIfNull(resource);

        try
        {
            // Use a cache key based on the resource type from the ViewDefinition
            var resourceType = viewDefinitionNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";
            var cacheKey = $"{resourceType}_{viewDefinitionNode.GetHashCode()}";

            // Get or compile the ViewDefinitionExpression
            if (!_compiledViewDefinitions.TryGetValue(cacheKey, out var viewExpr))
            {
                viewExpr = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);
                _compiledViewDefinitions[cacheKey] = viewExpr;
            }

            // Use the visitor to evaluate
            return _visitor.Evaluate(viewExpr, resource);
        }
        catch (Exception ex)
        {
            var resourceType = viewDefinitionNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";
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
