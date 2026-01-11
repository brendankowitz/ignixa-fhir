/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Visitor interface for traversing FhirPath parse trees.
 * Enables separation of parsing from AST construction.
 */

namespace Ignixa.FhirPath.Parsing.ParseTree;

/// <summary>
/// Visitor interface for traversing FhirPath parse trees.
/// Provides 13 visit methods for all parse node types.
/// </summary>
/// <typeparam name="TContext">The context type passed during traversal</typeparam>
/// <typeparam name="TResult">The result type produced by visiting nodes</typeparam>
internal interface IParseTreeVisitor<TContext, TResult>
{
    TResult VisitBinary(BinaryParseNode node, TContext context);
    TResult VisitUnary(UnaryParseNode node, TContext context);
    TResult VisitFunctionCall(FunctionCallParseNode node, TContext context);
    TResult VisitChild(ChildParseNode node, TContext context);
    TResult VisitConstant(ConstantParseNode node, TContext context);
    TResult VisitIdentifier(IdentifierParseNode node, TContext context);
    TResult VisitPropertyAccess(PropertyAccessParseNode node, TContext context);
    TResult VisitVariable(VariableRefParseNode node, TContext context);
    TResult VisitIndexer(IndexerParseNode node, TContext context);
    TResult VisitParenthesized(ParenthesizedParseNode node, TContext context);
    TResult VisitQuantity(QuantityParseNode node, TContext context);
    TResult VisitScope(ScopeParseNode node, TContext context);
    TResult VisitEmpty(EmptyParseNode node, TContext context);
}
