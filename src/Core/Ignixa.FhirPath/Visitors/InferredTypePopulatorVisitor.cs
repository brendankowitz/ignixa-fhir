// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Decorator visitor that wraps FhirPathAnalyzer to populate InferredType on each expression node.
/// This visitor ensures that child expressions are visited through itself (not the inner analyzer)
/// so that ALL nodes in the expression tree get their InferredType populated.
/// </summary>
internal sealed class InferredTypePopulatorVisitor : IFhirPathExpressionVisitor<AnalysisContext, FhirPathTypeSet>
{
    private readonly FhirPathAnalyzer _innerAnalyzer;

    public InferredTypePopulatorVisitor(IFhirSchemaProvider schema)
    {
        _innerAnalyzer = new FhirPathAnalyzer(schema);
    }

    private FhirPathTypeSet VisitAndPopulate(Expression expression, AnalysisContext context,
        Func<FhirPathTypeSet> visitFunc)
    {
        var result = visitFunc();
        expression.InferredType = result.ToString();
        return result;
    }

    /// <summary>
    /// Visits a child expression through this populator to ensure it gets InferredType populated.
    /// </summary>
    private void VisitChildExpression(Expression? child, AnalysisContext context)
    {
        child?.AcceptVisitor(this, context);
    }

    public FhirPathTypeSet VisitScope(ScopeExpression expression, AnalysisContext context)
    {
        // ScopeExpression has no child expressions (just $this, $index, etc.)
        return VisitAndPopulate(expression, context, 
            () => _innerAnalyzer.VisitScope(expression, context));
    }

    public FhirPathTypeSet VisitBinary(BinaryExpression expression, AnalysisContext context)
    {
        // Visit left and right operands first
        VisitChildExpression(expression.Left, context);
        VisitChildExpression(expression.Right, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitBinary(expression, context));
    }

    public FhirPathTypeSet VisitUnary(UnaryExpression expression, AnalysisContext context)
    {
        // Visit operand first
        VisitChildExpression(expression.Operand, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitUnary(expression, context));
    }

    public FhirPathTypeSet VisitFunctionCall(FunctionCallExpression expression, AnalysisContext context)
    {
        // Visit focus and all arguments first
        VisitChildExpression(expression.Focus, context);
        foreach (var arg in expression.Arguments)
        {
            VisitChildExpression(arg, context);
        }
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitFunctionCall(expression, context));
    }

    public FhirPathTypeSet VisitChild(ChildExpression expression, AnalysisContext context)
    {
        // Visit focus first
        VisitChildExpression(expression.Focus, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitChild(expression, context));
    }

    public FhirPathTypeSet VisitConstant(ConstantExpression expression, AnalysisContext context)
    {
        // No children to visit
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitConstant(expression, context));
    }

    public FhirPathTypeSet VisitIdentifier(IdentifierExpression expression, AnalysisContext context)
    {
        // No children to visit
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitIdentifier(expression, context));
    }

    public FhirPathTypeSet VisitVariable(VariableRefExpression expression, AnalysisContext context)
    {
        // No children to visit
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitVariable(expression, context));
    }

    public FhirPathTypeSet VisitIndexer(IndexerExpression expression, AnalysisContext context)
    {
        // Visit collection and index first
        VisitChildExpression(expression.Collection, context);
        VisitChildExpression(expression.Index, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitIndexer(expression, context));
    }

    public FhirPathTypeSet VisitParenthesized(ParenthesizedExpression expression, AnalysisContext context)
    {
        // Visit inner expression first
        VisitChildExpression(expression.InnerExpression, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitParenthesized(expression, context));
    }

    public FhirPathTypeSet VisitQuantity(QuantityExpression expression, AnalysisContext context)
    {
        // No children to visit
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitQuantity(expression, context));
    }

    public FhirPathTypeSet VisitEmpty(EmptyExpression expression, AnalysisContext context)
    {
        // No children to visit
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitEmpty(expression, context));
    }

    public FhirPathTypeSet VisitPropertyAccess(PropertyAccessExpression expression, AnalysisContext context)
    {
        // Visit focus first
        VisitChildExpression(expression.Focus, context);
        
        return VisitAndPopulate(expression, context,
            () => _innerAnalyzer.VisitPropertyAccess(expression, context));
    }
}
