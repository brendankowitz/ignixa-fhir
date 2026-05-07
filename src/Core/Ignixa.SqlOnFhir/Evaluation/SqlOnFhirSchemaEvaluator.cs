/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Schema evaluator for extracting output column schema from ViewDefinitions.
 * Uses visitor pattern to traverse ViewDefinition expressions and determine output columns.
 */

using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Expressions;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Column schema information extracted from a ViewDefinition.
/// </summary>
public record ColumnSchema(
    string Name,
    string? Type,
    bool Collection,
    IReadOnlyList<(string Name, string Value)>? Tags = null);

/// <summary>
/// Evaluates SQL on FHIR v2 ViewDefinitions to extract output schema (column names and types).
/// Uses visitor pattern to properly handle nested selects, forEach unnesting, and unionAll operations.
/// </summary>
public class SqlOnFhirSchemaEvaluator
{
    private readonly SqlOnFhirSchemaVisitor _visitor;
    private readonly Dictionary<string, IReadOnlyList<ColumnSchema>> _compiledSchemas = [];

    public SqlOnFhirSchemaEvaluator()
    {
        _visitor = new SqlOnFhirSchemaVisitor();
    }

    /// <summary>
    /// Extracts the output schema (columns with names and types) from a ViewDefinition expression.
    /// </summary>
    /// <param name="viewDefinitionExpression">The parsed ViewDefinition expression</param>
    /// <returns>List of columns that will be in the output</returns>
    public IReadOnlyList<ColumnSchema> GetSchema(ViewDefinitionExpression viewDefinitionExpression)
    {
        ArgumentNullException.ThrowIfNull(viewDefinitionExpression);

        try
        {
            // Use a cache key based on the resource type and expression hash
            var cacheKey = $"{viewDefinitionExpression.Resource}_{viewDefinitionExpression.GetHashCode()}";

            // Get or compile the schema
            if (!_compiledSchemas.TryGetValue(cacheKey, out var schema))
            {
                // Extract schema using visitor
                schema = _visitor.ExtractSchema(viewDefinitionExpression);
                _compiledSchemas[cacheKey] = schema;
            }

            return schema;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to extract schema from ViewDefinition for resource type '{viewDefinitionExpression.Resource}'",
                ex);
        }
    }

    /// <summary>
    /// Clears the compiled schema cache.
    /// </summary>
    public void ClearCache()
    {
        _compiledSchemas.Clear();
    }
}
