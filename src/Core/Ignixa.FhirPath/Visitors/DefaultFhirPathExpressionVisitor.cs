// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Base visitor implementation providing default traversal logic for FhirPath expression trees.
/// Derived classes override only the visit methods they need to customize.
/// Provides 14 virtual methods for all expression types in the FhirPath AST.
/// </summary>
/// <typeparam name="TContext">The context type passed during traversal</typeparam>
/// <typeparam name="TOutput">The output type produced by visiting expressions</typeparam>
/// <remarks>
/// <para>
/// <b>Context Management for Type Inference Visitors:</b>
/// </para>
/// <para>
/// When implementing a type inference or validation visitor, use <see cref="FhirPathVisitorContext"/>
/// as your <typeparamref name="TContext"/> type. This provides:
/// </para>
/// <list type="bullet">
///   <item><description>Immutable context stacks for safe traversal</description></item>
///   <item><description>Built-in variable and scope resolution</description></item>
///   <item><description>Proper $this/$that/$total/$index handling</description></item>
/// </list>
/// <para>
/// <b>Handling Nested Expressions (where, select, aggregate):</b>
/// </para>
/// <para>
/// When visiting function calls that take expression arguments, you need to push
/// appropriate context before visiting the arguments:
/// </para>
/// <code>
/// public override FhirPathTypeSet VisitFunctionCall(
///     FunctionCallExpression expr,
///     FhirPathVisitorContext context)
/// {
///     var focusResult = Visit(expr.Focus, context);
///
///     if (expr.FunctionName is "where" or "select" or "exists" or "all")
///     {
///         // Arguments evaluate on SINGLE ITEM context (not collection)
///         var innerContext = context
///             .PushPropertyContext(focusResult)
///             .PushExpressionContext(focusResult.AsSingle());
///
///         foreach (var arg in expr.Arguments)
///         {
///             Visit(arg, innerContext);
///         }
///     }
///     // ... handle return type
/// }
/// </code>
/// <para>
/// <b>Scope Resolution:</b>
/// </para>
/// <para>
/// Override <see cref="VisitScope"/> to resolve $this, $that, $total, $index:
/// </para>
/// <code>
/// public override FhirPathTypeSet VisitScope(
///     ScopeExpression expr,
///     FhirPathVisitorContext context)
/// {
///     return context.ResolveScope(expr.ScopeName) ?? new FhirPathTypeSet();
/// }
/// </code>
/// <para>
/// <b>Variable Resolution:</b>
/// </para>
/// <para>
/// Override <see cref="VisitVariable"/> to resolve %resource, %context, etc.:
/// </para>
/// <code>
/// public override FhirPathTypeSet VisitVariable(
///     VariableRefExpression expr,
///     FhirPathVisitorContext context)
/// {
///     return context.ResolveVariable(expr.Name) ?? new FhirPathTypeSet();
/// }
/// </code>
/// </remarks>
public abstract class DefaultFhirPathExpressionVisitor<TContext, TOutput> : IFhirPathExpressionVisitor<TContext, TOutput>
{
    public virtual TOutput VisitScope(ScopeExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitBinary(BinaryExpression expression, TContext context)
    {
        // Default: visit left and right operands
        expression.Left?.AcceptVisitor(this, context);
        expression.Right?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitUnary(UnaryExpression expression, TContext context)
    {
        // Default: visit operand
        expression.Operand?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitFunctionCall(FunctionCallExpression expression, TContext context)
    {
        // Default: visit focus and all arguments
        expression.Focus?.AcceptVisitor(this, context);
        foreach (var arg in expression.Arguments)
        {
            arg?.AcceptVisitor(this, context);
        }
        return default!;
    }

    public virtual TOutput VisitChild(ChildExpression expression, TContext context)
    {
        // Default: visit focus
        expression.Focus?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitConstant(ConstantExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitIdentifier(IdentifierExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitVariable(VariableRefExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitIndexer(IndexerExpression expression, TContext context)
    {
        // Default: visit collection and index
        expression.Collection?.AcceptVisitor(this, context);
        expression.Index?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitParenthesized(ParenthesizedExpression expression, TContext context)
    {
        // Default: visit inner expression
        expression.InnerExpression?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitQuantity(QuantityExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitEmpty(EmptyExpression expression, TContext context)
    {
        // Default: return default value (no children to visit)
        return default!;
    }

    public virtual TOutput VisitPropertyAccess(PropertyAccessExpression expression, TContext context)
    {
        // Default: visit focus (property name is just a string, not a child expression)
        expression.Focus?.AcceptVisitor(this, context);
        return default!;
    }

    public virtual TOutput VisitInstanceSelector(InstanceSelectorExpression expression, TContext context)
    {
        // Default: visit all element value expressions
        foreach (var element in expression.Elements)
        {
            element.ValueExpression?.AcceptVisitor(this, context);
        }
        return default!;
    }
}
