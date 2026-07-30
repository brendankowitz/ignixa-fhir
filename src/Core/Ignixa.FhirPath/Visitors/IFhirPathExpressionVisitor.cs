// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Visitor interface for traversing FhirPath expression trees.
/// Provides 14 visit methods for all expression types in the FhirPath AST.
/// </summary>
/// <typeparam name="TContext">The context type passed during traversal</typeparam>
/// <typeparam name="TOutput">The output type produced by visiting expressions</typeparam>
public interface IFhirPathExpressionVisitor<TContext, TOutput>
{
    TOutput VisitScope(ScopeExpression expression, TContext context);
    TOutput VisitBinary(BinaryExpression expression, TContext context);
    TOutput VisitUnary(UnaryExpression expression, TContext context);
    TOutput VisitFunctionCall(FunctionCallExpression expression, TContext context);
    TOutput VisitChild(ChildExpression expression, TContext context);
    TOutput VisitConstant(ConstantExpression expression, TContext context);
    TOutput VisitIdentifier(IdentifierExpression expression, TContext context);
    TOutput VisitVariable(VariableRefExpression expression, TContext context);
    TOutput VisitIndexer(IndexerExpression expression, TContext context);
    TOutput VisitParenthesized(ParenthesizedExpression expression, TContext context);
    TOutput VisitQuantity(QuantityExpression expression, TContext context);
    TOutput VisitEmpty(EmptyExpression expression, TContext context);
    TOutput VisitPropertyAccess(PropertyAccessExpression expression, TContext context);
    TOutput VisitInstanceSelector(InstanceSelectorExpression expression, TContext context);
}
