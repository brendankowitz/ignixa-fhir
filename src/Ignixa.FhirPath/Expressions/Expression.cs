/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Sparky Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Base class for all FhirPath expression nodes in the abstract syntax tree (AST).
/// </summary>
public abstract class Expression
{
    protected Expression()
    {
    }

    protected Expression(ISourcePositionInfo? location)
    {
        Location = location;
    }

    /// <summary>
    /// Location information for this expression component in the parsed FhirPath expression.
    /// </summary>
    public ISourcePositionInfo? Location { get; set; }

    /// <summary>
    /// Original source text for this expression (preserves whitespace and comments for round-tripping).
    /// Only populated when FhirPathCompiler is constructed with preserveTrivia = true.
    /// </summary>
    public string? SourceText { get; set; }

    /// <summary>
    /// Sets position information and returns this expression for fluent chaining.
    /// </summary>
    public T SetPosition<T>(ISourcePositionInfo location) where T : Expression
    {
        Location = location;
        return (T)this;
    }

    /// <summary>
    /// Converts this expression back to FhirPath syntax.
    /// If SourceText is available (preserveTrivia mode), returns the original text.
    /// Otherwise, reconstructs from the AST using ToString().
    /// </summary>
    public string ToFhirPath() => SourceText ?? ToString() ?? string.Empty;
}

/// <summary>
/// Represents a constant value in a FhirPath expression.
/// Examples: 42, 3.14, 'hello', true, @2024-01-15
/// </summary>
public class ConstantExpression : Expression
{
    public ConstantExpression(object value, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public object Value { get; }

    public override string ToString() => $"Constant({Value})";
}

/// <summary>
/// Represents an identifier in a FhirPath expression.
/// Examples: name, Patient, given
/// </summary>
public class IdentifierExpression : Expression
{
    public IdentifierExpression(string name, ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public override string ToString() => $"Identifier({Name})";
}

/// <summary>
/// Represents an axis reference in a FhirPath expression.
/// Examples: $this, $index, $total
/// </summary>
public class AxisExpression : Expression
{
    public AxisExpression(string axisName, ISourcePositionInfo? location = null) : base(location)
    {
        AxisName = axisName ?? throw new ArgumentNullException(nameof(axisName));
    }

    public string AxisName { get; }

    public override string ToString() => $"Axis(${AxisName})";

    // Common axis instances
    public static readonly AxisExpression This = new("this");
    public static readonly AxisExpression Index = new("index");
    public static readonly AxisExpression Total = new("total");
    public static readonly AxisExpression That = new("that");
}

/// <summary>
/// Represents a variable reference in a FhirPath expression.
/// Examples: %context, %resource, %ext-id
/// </summary>
public class VariableRefExpression : Expression
{
    public VariableRefExpression(string name, ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public override string ToString() => $"Variable(%{Name})";
}

/// <summary>
/// Represents a function call in a FhirPath expression.
/// Examples: exists(), where($this > 5), count()
/// </summary>
public class FunctionCallExpression : Expression
{
    public FunctionCallExpression(
        Expression? focus,
        string functionName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        Focus = focus;
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Arguments = arguments?.ToList() ?? new List<Expression>();
    }

    /// <summary>The expression this function is called on (left of dot), or null for root context</summary>
    public Expression? Focus { get; }

    /// <summary>Name of the function being called</summary>
    public string FunctionName { get; }

    /// <summary>Arguments passed to the function</summary>
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() =>
        Focus != null
            ? $"{Focus}.{FunctionName}({string.Join(", ", Arguments)})"
            : $"{FunctionName}({string.Join(", ", Arguments)})";
}

/// <summary>
/// Represents member access / child navigation in a FhirPath expression.
/// Examples: Patient.name, name.given
/// </summary>
public class ChildExpression : FunctionCallExpression
{
    public ChildExpression(Expression? focus, string childName, ISourcePositionInfo? location = null)
        : base(focus, "builtin.children", new[] { new ConstantExpression(childName) }, location)
    {
        ChildName = childName;
    }

    public string ChildName { get; }

    public override string ToString() =>
        Focus != null ? $"{Focus}.{ChildName}" : ChildName;
}

/// <summary>
/// Represents indexer access in a FhirPath expression.
/// Examples: name[0], collection[5]
/// </summary>
public class IndexerExpression : FunctionCallExpression
{
    public IndexerExpression(Expression collection, Expression index, ISourcePositionInfo? location = null)
        : base(collection, "builtin.item", new[] { index }, location)
    {
    }

    public Expression Collection => Focus!;
    public Expression Index => Arguments[0];

    public override string ToString() => $"{Collection}[{Index}]";
}

/// <summary>
/// Represents a binary operation in a FhirPath expression.
/// Examples: age > 18, name = 'John', 1 + 2
/// </summary>
public class BinaryExpression : FunctionCallExpression
{
    public BinaryExpression(string op, Expression left, Expression right, ISourcePositionInfo? location = null)
        : base(AxisExpression.That, $"binary.{op}", new[] { left, right }, location)
    {
        Operator = op;
    }

    public string Operator { get; }
    public Expression Left => Arguments[0];
    public Expression Right => Arguments[1];

    public override string ToString() => $"({Left} {Operator} {Right})";
}

/// <summary>
/// Represents a unary operation in a FhirPath expression.
/// Examples: -5, +10
/// </summary>
public class UnaryExpression : FunctionCallExpression
{
    public UnaryExpression(string op, Expression operand, ISourcePositionInfo? location = null)
        : base(AxisExpression.That, $"unary.{op}", new[] { operand }, location)
    {
        Operator = op;
    }

    public string Operator { get; }
    public Expression Operand => Arguments[0];

    public override string ToString() => $"({Operator}{Operand})";
}

/// <summary>
/// Represents a parenthesized expression in a FhirPath expression.
/// Examples: (1 + 2), (name.exists())
/// </summary>
public class ParenthesizedExpression : Expression
{
    public ParenthesizedExpression(Expression innerExpression, ISourcePositionInfo? location = null)
        : base(location)
    {
        InnerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
    }

    public Expression InnerExpression { get; }

    public override string ToString() => $"({InnerExpression})";
}

/// <summary>
/// Represents an empty collection literal in a FhirPath expression.
/// Example: {}
/// </summary>
public class EmptyExpression : Expression
{
    public EmptyExpression(ISourcePositionInfo? location = null) : base(location)
    {
    }

    public override string ToString() => "{}";
}

/// <summary>
/// Represents a quantity literal with a value and UCUM unit in a FhirPath expression.
/// Examples: 5 'mg', 37.5 'Cel', 100 '[lb_av]'
/// </summary>
public class QuantityExpression : Expression
{
    public QuantityExpression(decimal value, string unit, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value;
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
    }

    /// <summary>Numeric value of the quantity</summary>
    public decimal Value { get; }

    /// <summary>UCUM unit code</summary>
    public string Unit { get; }

    public override string ToString() => $"{Value} '{Unit}'";
}
