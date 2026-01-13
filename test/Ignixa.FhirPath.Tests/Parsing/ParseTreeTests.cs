/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for parse tree construction and visitor-based compilation.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;
using Ignixa.FhirPath.Parsing.ParseTree;

namespace Ignixa.FhirPath.Tests.Parsing;

public class ParseTreeTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathParser _optimizingParser = new(CompilationOptions.Optimized);

    [Fact]
    public void GivenSimpleIdentifier_WhenParsingToTree_ThenReturnsPropertyAccessParseNode()
    {
        var tree = _parser.ParseToTree("Patient");

        Assert.IsType<PropertyAccessParseNode>(tree);
        var prop = (PropertyAccessParseNode)tree;
        Assert.Equal("Patient", prop.PropertyName);
        Assert.Null(prop.Focus);
    }

    [Fact]
    public void GivenIntegerLiteral_WhenParsingToTree_ThenReturnsConstantParseNode()
    {
        var tree = _parser.ParseToTree("42");

        Assert.IsType<ConstantParseNode>(tree);
        var constant = (ConstantParseNode)tree;
        Assert.Equal(42, constant.Value);
    }

    [Fact]
    public void GivenStringLiteral_WhenParsingToTree_ThenReturnsConstantParseNode()
    {
        var tree = _parser.ParseToTree("'hello'");

        Assert.IsType<ConstantParseNode>(tree);
        var constant = (ConstantParseNode)tree;
        Assert.Equal("hello", constant.Value);
    }

    [Fact]
    public void GivenBinaryExpression_WhenParsingToTree_ThenReturnsBinaryParseNode()
    {
        var tree = _parser.ParseToTree("1 + 2");

        Assert.IsType<BinaryParseNode>(tree);
        var binary = (BinaryParseNode)tree;
        Assert.Equal("+", binary.Operator);
        Assert.IsType<ConstantParseNode>(binary.Left);
        Assert.IsType<ConstantParseNode>(binary.Right);
    }

    [Fact]
    public void GivenUnaryExpression_WhenParsingToTree_ThenReturnsUnaryParseNode()
    {
        var tree = _parser.ParseToTree("-5");

        Assert.IsType<UnaryParseNode>(tree);
        var unary = (UnaryParseNode)tree;
        Assert.Equal("-", unary.Operator);
        Assert.IsType<ConstantParseNode>(unary.Operand);
    }

    [Fact]
    public void GivenFunctionCall_WhenParsingToTree_ThenReturnsFunctionCallParseNode()
    {
        var tree = _parser.ParseToTree("name.exists()");

        Assert.IsType<FunctionCallParseNode>(tree);
        var func = (FunctionCallParseNode)tree;
        Assert.Equal("exists", func.FunctionName);
        Assert.Empty(func.Arguments);
        Assert.IsType<PropertyAccessParseNode>(func.Focus);
    }

    [Fact]
    public void GivenChildNavigation_WhenParsingToTree_ThenReturnsChildParseNode()
    {
        var tree = _parser.ParseToTree("Patient.name");

        Assert.IsType<ChildParseNode>(tree);
        var child = (ChildParseNode)tree;
        Assert.Equal("name", child.ChildName);
        Assert.IsType<PropertyAccessParseNode>(child.Focus);
    }

    [Fact]
    public void GivenIndexer_WhenParsingToTree_ThenReturnsIndexerParseNode()
    {
        var tree = _parser.ParseToTree("name[0]");

        Assert.IsType<IndexerParseNode>(tree);
        var indexer = (IndexerParseNode)tree;
        Assert.IsType<PropertyAccessParseNode>(indexer.Collection);
        Assert.IsType<ConstantParseNode>(indexer.Index);
    }

    [Fact]
    public void GivenParenthesized_WhenParsingToTree_ThenReturnsParenthesizedParseNode()
    {
        var tree = _parser.ParseToTree("(1 + 2)");

        Assert.IsType<ParenthesizedParseNode>(tree);
        var paren = (ParenthesizedParseNode)tree;
        Assert.IsType<BinaryParseNode>(paren.InnerExpression);
    }

    [Fact]
    public void GivenScope_WhenParsingToTree_ThenReturnsScopeParseNode()
    {
        var tree = _parser.ParseToTree("$this");

        Assert.IsType<ScopeParseNode>(tree);
        var scope = (ScopeParseNode)tree;
        Assert.Equal("this", scope.ScopeName);
    }

    [Fact]
    public void GivenVariable_WhenParsingToTree_ThenReturnsVariableRefParseNode()
    {
        var tree = _parser.ParseToTree("%context");

        Assert.IsType<VariableRefParseNode>(tree);
        var varRef = (VariableRefParseNode)tree;
        Assert.Equal("context", varRef.Name);
    }

    [Fact]
    public void GivenEmptyCollection_WhenParsingToTree_ThenReturnsEmptyParseNode()
    {
        var tree = _parser.ParseToTree("{}");

        Assert.IsType<EmptyParseNode>(tree);
    }

    [Fact]
    public void GivenQuantity_WhenParsingToTree_ThenReturnsQuantityParseNode()
    {
        var tree = _parser.ParseToTree("5 'mg'");

        Assert.IsType<QuantityParseNode>(tree);
        var qty = (QuantityParseNode)tree;
        Assert.Equal(5m, qty.Value);
        Assert.Equal("mg", qty.Unit);
    }

    [Fact]
    public void GivenOfType_WhenParsingToTree_ThenArgumentIsIdentifierParseNode()
    {
        var tree = _parser.ParseToTree("value.ofType(string)");

        Assert.IsType<FunctionCallParseNode>(tree);
        var func = (FunctionCallParseNode)tree;
        Assert.Equal("ofType", func.FunctionName);
        Assert.Single(func.Arguments);
        Assert.IsType<IdentifierParseNode>(func.Arguments[0]);
    }

    [Fact]
    public void GivenParseTree_WhenBuildingAst_ThenProducesCorrectExpression()
    {
        var tree = _parser.ParseToTree("1 + 2");
        var ast = _parser.BuildAst(tree);

        Assert.IsType<BinaryExpression>(ast);
        var binary = (BinaryExpression)ast;
        Assert.Equal("+", binary.Operator);
    }

    [Fact]
    public void GivenParseTree_WhenBuildingWithOptimization_ThenFoldsConstants()
    {
        var ast = _optimizingParser.Parse("1 + 2");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(3, constant.Value);
    }

    [Fact]
    public void GivenMultiplication_WhenBuildingWithOptimization_ThenFoldsConstants()
    {
        var ast = _optimizingParser.Parse("3 * 4");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(12, constant.Value);
    }

    [Fact]
    public void GivenNestedArithmetic_WhenBuildingWithOptimization_ThenFoldsCompletely()
    {
        var ast = _optimizingParser.Parse("(2 + 3) * 4");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(20, constant.Value);
    }

    [Fact]
    public void GivenUnaryNegation_WhenBuildingWithOptimization_ThenFoldsConstant()
    {
        var ast = _optimizingParser.Parse("-5");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(-5, constant.Value);
    }

    [Fact]
    public void GivenStringConcat_WhenBuildingWithOptimization_ThenFoldsStrings()
    {
        var ast = _optimizingParser.Parse("'hello' & ' world'");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal("hello world", constant.Value);
    }

    [Fact]
    public void GivenBooleanAnd_WhenBuildingWithOptimization_ThenFoldsBooleans()
    {
        var ast = _optimizingParser.Parse("true and false");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(false, constant.Value);
    }

    [Fact]
    public void GivenEquality_WhenBuildingWithOptimization_ThenFoldsEquality()
    {
        var ast = _optimizingParser.Parse("5 = 5");

        Assert.IsType<ConstantExpression>(ast);
        var constant = (ConstantExpression)ast;
        Assert.Equal(true, constant.Value);
    }

    [Fact]
    public void GivenDivisionByZero_WhenBuildingWithOptimization_ThenDoesNotFold()
    {
        var ast = _optimizingParser.Parse("5 / 0");

        Assert.IsType<BinaryExpression>(ast);
    }

    [Fact]
    public void GivenMixedExpression_WhenBuildingWithOptimization_ThenPartialFold()
    {
        var ast = _optimizingParser.Parse("name and (1 + 1 = 2)");

        // With enhanced optimization: "(1 + 1 = 2)" becomes "true", then "name and true" becomes "name"
        Assert.IsType<PropertyAccessExpression>(ast);
        Assert.Equal("name", ((PropertyAccessExpression)ast).PropertyName);
    }

    [Fact]
    public void GivenParseTreeNode_WhenAcceptingVisitor_ThenVisitorIsCalled()
    {
        var tree = _parser.ParseToTree("1 + 2 * 3");
        var visitor = new CountingParseTreeVisitor();

        tree.Accept(visitor, default);

        Assert.Equal(2, visitor.BinaryCount);
        Assert.Equal(3, visitor.ConstantCount);
    }

    [Fact]
    public void GivenComplexExpression_WhenParsingThenBuilding_ThenProducesSameResultAsDirectParse()
    {
        var expr = "Patient.name.where(use = 'official').given.first()";

        var direct = _parser.Parse(expr);
        var tree = _parser.ParseToTree(expr);
        var built = _parser.BuildAst(tree);

        Assert.Equal(direct.ToString(), built.ToString());
    }

    [Fact]
    public void GivenTryParseToTree_WhenValid_ThenReturnsTrue()
    {
        var success = _parser.TryParseToTree("Patient.name", out var result, out var error);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public void GivenTryParseToTree_WhenInvalid_ThenReturnsFalse()
    {
        var success = _parser.TryParseToTree("Patient..name", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void GivenSourceLocation_WhenCreatedFromToken_ThenHasCorrectValues()
    {
        var tree = _parser.ParseToTree("Patient");

        Assert.IsType<PropertyAccessParseNode>(tree);
        var prop = (PropertyAccessParseNode)tree;
        Assert.Equal(1, prop.Location.Line);
        Assert.Equal(1, prop.Location.Column);
    }

    private class CountingParseTreeVisitor : IParseTreeVisitor<object?, int>
    {
        public int BinaryCount { get; private set; }
        public int ConstantCount { get; private set; }

        public int VisitBinary(BinaryParseNode node, object? context)
        {
            BinaryCount++;
            node.Left.Accept(this, context);
            node.Right.Accept(this, context);
            return 0;
        }

        public int VisitConstant(ConstantParseNode node, object? context)
        {
            ConstantCount++;
            return 0;
        }

        public int VisitUnary(UnaryParseNode node, object? context)
        {
            node.Operand.Accept(this, context);
            return 0;
        }

        public int VisitFunctionCall(FunctionCallParseNode node, object? context)
        {
            node.Focus?.Accept(this, context);
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this, context);
            }
            return 0;
        }

        public int VisitChild(ChildParseNode node, object? context)
        {
            node.Focus.Accept(this, context);
            return 0;
        }

        public int VisitIdentifier(IdentifierParseNode node, object? context) => 0;
        public int VisitPropertyAccess(PropertyAccessParseNode node, object? context)
        {
            node.Focus?.Accept(this, context);
            return 0;
        }
        public int VisitVariable(VariableRefParseNode node, object? context) => 0;
        public int VisitIndexer(IndexerParseNode node, object? context)
        {
            node.Collection.Accept(this, context);
            node.Index.Accept(this, context);
            return 0;
        }
        public int VisitParenthesized(ParenthesizedParseNode node, object? context)
        {
            node.InnerExpression.Accept(this, context);
            return 0;
        }
        public int VisitQuantity(QuantityParseNode node, object? context) => 0;
        public int VisitScope(ScopeParseNode node, object? context) => 0;
        public int VisitEmpty(EmptyParseNode node, object? context) => 0;
        public int VisitInstanceSelector(InstanceSelectorParseNode node, object? context)
        {
            foreach (var element in node.Elements)
            {
                element.ValueExpression.Accept(this, context);
            }
            return 0;
        }
    }
}
