/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Visitor implementation for extracting schema from SQL on FHIR ViewDefinition expressions.
 * Properly handles nested selects (Cartesian product), forEach unnesting, and unionAll operations.
 */

using System.Collections.Immutable;
using Ignixa.SqlOnFhir.Expressions;

namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// Visitor that extracts schema information from ViewDefinition expressions.
/// Handles the same complex logic as SqlOnFhirEvaluationVisitor but returns schema instead of data.
/// </summary>
internal class SqlOnFhirSchemaVisitor : ISqlOnFhirExpressionVisitor<IReadOnlyList<ColumnSchema>>
{
    /// <summary>
    /// Extracts the complete output schema from a ViewDefinition expression.
    /// </summary>
    public IReadOnlyList<ColumnSchema> ExtractSchema(ViewDefinitionExpression viewDef)
    {
        return viewDef.Accept(this);
    }

    /// <summary>
    /// Visits a ViewDefinition expression and returns the output schema.
    /// Handles multiple SELECT groups with Cartesian product semantics.
    /// </summary>
    public IReadOnlyList<ColumnSchema> Visit(ViewDefinitionExpression node)
    {
        // WHERE clauses don't affect schema, only row filtering

        // Evaluate SELECT groups with proper column merging
        if (node.Select.IsEmpty)
        {
            return new List<ColumnSchema>();
        }

        // Start with first SELECT group (convert to mutable list for merging)
        var currentColumns = node.Select[0].Accept(this).ToList();

        // Merge subsequent SELECT groups
        // Per SQL on FHIR spec: Multiple select groups create Cartesian product of rows
        // but UNION the columns (all columns from all selects appear in output)
        for (int i = 1; i < node.Select.Length; i++)
        {
            var selectExpr = node.Select[i];
            var selectColumns = selectExpr.Accept(this);

            // Merge columns: add columns from this select that aren't already present
            foreach (var column in selectColumns)
            {
                if (!currentColumns.Any(c => c.Name == column.Name))
                {
                    currentColumns.Add(column);
                }
            }
        }

        return currentColumns;
    }

    /// <summary>
    /// Visits a SELECT expression and returns its columns.
    /// Handles forEach/forEachOrNull unnesting, repeat, nested selects, and unionAll.
    /// </summary>
    public IReadOnlyList<ColumnSchema> Visit(SelectExpression node)
    {
        // Start with direct columns from this SELECT
        var columns = new List<ColumnSchema>();

        foreach (var column in node.Columns)
        {
            columns.Add(column.Accept(this)[0]); // ColumnExpression returns single-item list
        }

        // Process nested SELECT groups (Cartesian product semantics)
        // Nested selects add their columns to the output
        foreach (var nestedSelect in node.NestedSelect)
        {
            var nestedColumns = nestedSelect.Accept(this);

            // Add columns from nested select that aren't already present
            foreach (var column in nestedColumns)
            {
                if (!columns.Any(c => c.Name == column.Name))
                {
                    columns.Add(column);
                }
            }
        }

        // Process UnionAll groups (concatenation semantics)
        // UnionAll adds additional rows with potentially different columns
        // All unionAll columns must also appear in the schema
        foreach (var unionAllGroup in node.UnionAll)
        {
            var unionColumns = unionAllGroup.Accept(this);

            // Add columns from unionAll that aren't already present
            foreach (var column in unionColumns)
            {
                if (!columns.Any(c => c.Name == column.Name))
                {
                    columns.Add(column);
                }
            }
        }

        // Note: forEach/forEachOrNull/repeat don't change the schema,
        // they only affect how many rows are generated (unnesting)
        // The same columns appear whether forEach returns 0, 1, or N items

        return columns;
    }

    /// <summary>
    /// Visits a column expression and returns its schema.
    /// </summary>
    public IReadOnlyList<ColumnSchema> Visit(ColumnExpression node)
    {
        // Return single column schema
        return new List<ColumnSchema>
        {
            new ColumnSchema(node.Name, node.Type, node.Collection)
        };
    }

    /// <summary>
    /// Visits a WHERE expression (doesn't contribute to schema).
    /// </summary>
    public IReadOnlyList<ColumnSchema> Visit(WhereExpression node)
    {
        // WHERE clauses filter rows but don't affect column schema
        return new List<ColumnSchema>();
    }

    /// <summary>
    /// Visits a constant expression (doesn't contribute to schema).
    /// </summary>
    public IReadOnlyList<ColumnSchema> Visit(ConstantExpression node)
    {
        // Constants are used in FHIRPath expressions but don't create output columns
        return new List<ColumnSchema>();
    }
}
