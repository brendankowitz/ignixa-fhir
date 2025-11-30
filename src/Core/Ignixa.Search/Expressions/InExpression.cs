// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents an 'in' expression where known values are grouped together.
/// Used to generate efficient SQL IN clauses instead of multiple OR conditions.
/// </summary>
/// <typeparam name="T">Type of the value included in the expression.</typeparam>
public class InExpression<T> : Expression, IFieldExpression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InExpression{T}"/> class.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="componentIndex">The component index.</param>
    /// <param name="values">The enumerable of values.</param>
    public InExpression(FieldName fieldName, int? componentIndex, IEnumerable<T> values)
        : this(fieldName, componentIndex)
    {
        Values = EnsureArg.HasItems(values?.ToArray(), nameof(values));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InExpression{T}"/> class.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="componentIndex">The component index.</param>
    /// <param name="values">The read-only list of values.</param>
    public InExpression(FieldName fieldName, int? componentIndex, IReadOnlyList<T> values)
        : this(fieldName, componentIndex)
    {
        Values = EnsureArg.HasItems(values, nameof(values));
    }

    private InExpression(FieldName fieldName, int? componentIndex)
    {
        FieldName = fieldName;
        ComponentIndex = componentIndex;
    }

    /// <inheritdoc />
    public FieldName FieldName { get; }

    /// <inheritdoc />
    public int? ComponentIndex { get; }

    /// <summary>
    /// Gets the read-only list of values for the IN clause.
    /// </summary>
    public IReadOnlyList<T> Values { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitIn(this, context);
    }

    public override string ToString()
    {
        return $"({(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName} IN ({string.Join(", ", Values.Select(v => FormatValue(v)))}))";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(InExpression<T>));
        hashCode.Add(FieldName);
        hashCode.Add(ComponentIndex);
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        return other is InExpression<T> expression &&
               expression.FieldName == FieldName &&
               expression.ComponentIndex == ComponentIndex;
    }

    private static string FormatValue(T value)
    {
        return value switch
        {
            string s => $"'{s}'",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? "null"
        };
    }
}
