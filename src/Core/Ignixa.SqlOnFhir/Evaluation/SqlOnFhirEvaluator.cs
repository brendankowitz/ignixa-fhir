/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Public API for evaluating SQL on FHIR v2 ViewDefinitions.
 * Uses ISourceNavigator for proper handling of FHIR data and visitor pattern for evaluation.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
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
    // context, resource and rootResource are answered by EvaluationContext.TryGetEnvironmentVariable's
    // switch before it ever consults a caller-supplied variable; rowIndex is re-injected by
    // SqlOnFhirEvaluationVisitor after the variables loop and always wins. A variables entry using one of
    // these names has never had any effect - it was accepted and silently discarded. That silence is the
    // bug (issue #439): reject the call instead of pretending the value was honoured.
    private static readonly HashSet<string> EngineManagedVariableNames =
        new(StringComparer.Ordinal) { "context", "resource", "rootResource", "rowIndex" };

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
        ValidateVariables(variables);

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
        catch (FhirPathEvaluationException ex)
        {
            // Same message and inner exception as the general case, but the type stays distinguishable
            // so callers can tell an ill-formed ViewDefinition expression from an engine defect.
            throw new FhirPathEvaluationException(
                $"Failed to evaluate ViewDefinition for resource type '{resourceType}'",
                ex);
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

    /// <summary>
    /// Rejects a <paramref name="variables"/> entry whose name the engine manages itself.
    /// </summary>
    /// <remarks>
    /// Checked here, at the public boundary, and before the try/catch below: doing it inside
    /// <c>CreateEvaluationContext</c> would let the general <c>catch (Exception ex)</c> rewrap this as an
    /// <see cref="InvalidOperationException"/> saying evaluation "failed", burying both the exception type
    /// and the reason a caller needs to fix their own input. This is the only method in the class that
    /// binds caller-supplied variables into an evaluation - <see cref="Evaluate"/> forwards to
    /// <see cref="EvaluateBatch"/> rather than duplicating the check - so there is nowhere else a
    /// <paramref name="variables"/> entry can enter unchecked.
    /// </remarks>
    private static void ValidateVariables(IReadOnlyDictionary<string, string>? variables)
    {
        if (variables == null)
            return;

        foreach (var name in variables.Keys)
        {
            if (EngineManagedVariableNames.Contains(name))
                throw new ArgumentException(
                    $"Variable '{name}' is engine-managed and cannot be supplied by the caller.",
                    nameof(variables));
        }
    }
}
