/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath parser.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests;

public class FhirPathParserTests
{
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenStringLiteral_WhenParsing_ThenReturnsConstantExpression()
    {
        var expr = _parser.Parse("'hello'");

        Assert.IsType<ConstantExpression>(expr);
        var constant = (ConstantExpression)expr;
        Assert.Equal("hello", constant.Value);
    }

    [Fact]
    public void GivenIntegerLiteral_WhenParsing_ThenReturnsConstantExpression()
    {
        var expr = _parser.Parse("42");

        Assert.IsType<ConstantExpression>(expr);
        var constant = (ConstantExpression)expr;
        Assert.Equal(42, constant.Value);
    }

    [Fact]
    public void GivenDecimalLiteral_WhenParsing_ThenReturnsConstantExpression()
    {
        var expr = _parser.Parse("3.14");

        Assert.IsType<ConstantExpression>(expr);
        var constant = (ConstantExpression)expr;
        Assert.Equal(3.14m, constant.Value);
    }

    [Fact]
    public void GivenBooleanTrue_WhenParsing_ThenReturnsConstantExpression()
    {
        var expr = _parser.Parse("true");

        Assert.IsType<ConstantExpression>(expr);
        var constant = (ConstantExpression)expr;
        Assert.Equal(true, constant.Value);
    }

    [Fact]
    public void GivenIdentifier_WhenParsing_ThenReturnsPropertyAccess()
    {
        var expr = _parser.Parse("Patient");

        Assert.IsType<PropertyAccessExpression>(expr);
        var prop = (PropertyAccessExpression)expr;
        Assert.Equal("Patient", prop.PropertyName);
        Assert.Null(prop.Focus);
    }

    [Fact]
    public void GivenSimplePath_WhenParsing_ThenReturnsChildExpression()
    {
        var expr = _parser.Parse("Patient.name");

        Assert.IsType<ChildExpression>(expr);
        var child = (ChildExpression)expr;
        Assert.Equal("name", child.ChildName);
        Assert.NotNull(child.Focus);
    }

    [Fact]
    public void GivenNestedPath_WhenParsing_ThenReturnsNestedChildExpressions()
    {
        var expr = _parser.Parse("Patient.name.given");

        Assert.IsType<ChildExpression>(expr);
        var given = (ChildExpression)expr;
        Assert.Equal("given", given.ChildName);

        Assert.IsType<ChildExpression>(given.Focus);
        var name = (ChildExpression)given.Focus!;
        Assert.Equal("name", name.ChildName);
    }

    [Fact]
    public void GivenFunctionCall_WhenParsing_ThenReturnsFunctionCallExpression()
    {
        var expr = _parser.Parse("name.exists()");

        Assert.IsType<FunctionCallExpression>(expr);
        var func = (FunctionCallExpression)expr;
        Assert.Equal("exists", func.FunctionName);
        Assert.Empty(func.Arguments);
    }

    [Fact]
    public void GivenFunctionWithArgument_WhenParsing_ThenIncludesArgument()
    {
        var expr = _parser.Parse("name.where($this != '')");

        Assert.IsType<FunctionCallExpression>(expr);
        var func = (FunctionCallExpression)expr;
        Assert.Equal("where", func.FunctionName);
        Assert.Single(func.Arguments);

        var arg = func.Arguments[0];
        Assert.IsType<BinaryExpression>(arg);
    }

    [Fact]
    public void GivenBinaryExpression_WhenParsing_ThenReturnsBinaryExpression()
    {
        var expr = _parser.Parse("age > 18");

        Assert.IsType<BinaryExpression>(expr);
        var binary = (BinaryExpression)expr;
        Assert.Equal(">", binary.Operator);
        Assert.IsType<PropertyAccessExpression>(binary.Left);
        Assert.IsType<ConstantExpression>(binary.Right);
    }

    [Fact]
    public void GivenLogicalExpression_WhenParsing_ThenReturnsBinaryExpression()
    {
        var expr = _parser.Parse("active = true and gender = 'male'");

        Assert.IsType<BinaryExpression>(expr);
        var binary = (BinaryExpression)expr;
        Assert.Equal("and", binary.Operator);

        Assert.IsType<BinaryExpression>(binary.Left);
        Assert.IsType<BinaryExpression>(binary.Right);
    }

    [Fact]
    public void GivenParenthesizedExpression_WhenParsing_ThenReturnsParenthesizedExpression()
    {
        var expr = _parser.Parse("(1 + 2)");

        Assert.IsType<ParenthesizedExpression>(expr);
        var paren = (ParenthesizedExpression)expr;
        Assert.IsType<BinaryExpression>(paren.InnerExpression);
    }

    [Fact]
    public void GivenIndexerExpression_WhenParsing_ThenReturnsIndexerExpression()
    {
        var expr = _parser.Parse("name[0]");

        Assert.IsType<IndexerExpression>(expr);
        var indexer = (IndexerExpression)expr;
        Assert.IsType<PropertyAccessExpression>(indexer.Collection);
        Assert.IsType<ConstantExpression>(indexer.Index);
    }

    [Fact]
    public void GivenAxisReference_WhenParsing_ThenReturnsAxisExpression()
    {
        var expr = _parser.Parse("$this");

        Assert.IsType<ScopeExpression>(expr);
        var scope = (ScopeExpression)expr;
        Assert.Equal("this", scope.ScopeName);
    }

    [Fact]
    public void GivenExternalConstant_WhenParsing_ThenReturnsVariableRefExpression()
    {
        var expr = _parser.Parse("%context");

        Assert.IsType<VariableRefExpression>(expr);
        var varRef = (VariableRefExpression)expr;
        Assert.Equal("context", varRef.Name);
    }

    [Fact]
    public void GivenEmptyCollection_WhenParsing_ThenReturnsEmptyExpression()
    {
        var expr = _parser.Parse("{}");

        Assert.IsType<EmptyExpression>(expr);
    }

    [Fact]
    public void GivenUnaryExpression_WhenParsing_ThenReturnsUnaryExpression()
    {
        var expr = _parser.Parse("-5");

        Assert.IsType<UnaryExpression>(expr);
        var unary = (UnaryExpression)expr;
        Assert.Equal("-", unary.Operator);
        Assert.IsType<ConstantExpression>(unary.Operand);
    }

    [Fact]
    public void GivenOperatorPrecedence_WhenParsing_ThenRespectsOrder()
    {
        // 1 + 2 * 3 should parse as 1 + (2 * 3), not (1 + 2) * 3
        var expr = _parser.Parse("1 + 2 * 3");

        Assert.IsType<BinaryExpression>(expr);
        var add = (BinaryExpression)expr;
        Assert.Equal("+", add.Operator);

        Assert.IsType<ConstantExpression>(add.Left); // 1
        Assert.IsType<BinaryExpression>(add.Right); // 2 * 3

        var multiply = (BinaryExpression)add.Right;
        Assert.Equal("*", multiply.Operator);
    }

    [Fact]
    public void GivenComplexExpression_WhenParsing_ThenBuildsCorrectAST()
    {
        var expr = _parser.Parse("Patient.name.given.where($this != '')");

        // Should be: where function
        Assert.IsType<FunctionCallExpression>(expr);
        var where = (FunctionCallExpression)expr;
        Assert.Equal("where", where.FunctionName);

        // Focus should be: Patient.name.given chain
        Assert.IsType<ChildExpression>(where.Focus);
        var given = (ChildExpression)where.Focus!;
        Assert.Equal("given", given.ChildName);

        // Argument should be: $this != ''
        Assert.Single(where.Arguments);
        Assert.IsType<BinaryExpression>(where.Arguments[0]);
        var notEquals = (BinaryExpression)where.Arguments[0];
        Assert.Equal("!=", notEquals.Operator);
    }

    [Fact]
    public void GivenInvalidExpression_WhenParsing_ThenThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _parser.Parse("Patient..name"));
    }

    [Fact]
    public void GivenEmptyString_WhenParsing_ThenThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(""));
    }

    [Fact]
    public void GivenTryParse_WhenValid_ThenReturnsTrue()
    {
        var success = _parser.TryParse("Patient.name", out var result, out var error);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public void GivenTryParse_WhenInvalid_ThenReturnsFalse()
    {
        var success = _parser.TryParse("Patient..name", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void GivenContainsFunction_WhenParsing_ThenReturnsFunctionCallExpression()
    {
        // FHIRPath spec: contains() is a function that checks if a collection contains a value
        // Example from FHIR search parameters: ValueSet.expansion.contains.code
        var expr = _parser.Parse("ValueSet.expansion.contains.code");

        Assert.IsType<ChildExpression>(expr);
        var code = (ChildExpression)expr;
        Assert.Equal("code", code.ChildName);

        Assert.IsType<ChildExpression>(code.Focus);
        var contains = (ChildExpression)code.Focus!;
        Assert.Equal("contains", contains.ChildName);

        Assert.IsType<ChildExpression>(contains.Focus);
        var expansion = (ChildExpression)contains.Focus!;
        Assert.Equal("expansion", expansion.ChildName);
    }

    [Fact]
    public void GivenContainsAsFunctionCall_WhenParsing_ThenReturnsFunctionCallExpression()
    {
        // FHIRPath spec: contains() as a function (not property accessor)
        // Example: name.contains('John')
        var expr = _parser.Parse("name.contains('John')");

        Assert.IsType<FunctionCallExpression>(expr);
        var func = (FunctionCallExpression)expr;
        Assert.Equal("contains", func.FunctionName);
        Assert.Single(func.Arguments);

        Assert.IsType<ConstantExpression>(func.Arguments[0]);
        var arg = (ConstantExpression)func.Arguments[0];
        Assert.Equal("John", arg.Value);
    }

    [Fact]
    public void GivenAsOperator_WhenParsing_ThenReturnsAsExpression()
    {
        // FHIRPath spec: 'as' operator for type casting
        // Example: value as Quantity
        var expr = _parser.Parse("value as Quantity");

        // Expected: BinaryExpression with operand and type
        Assert.IsType<BinaryExpression>(expr);
        var asExpr = (BinaryExpression)expr;
        Assert.Equal("as", asExpr.Operator);
        Assert.IsType<PropertyAccessExpression>(asExpr.Left); // value
        Assert.IsType<PropertyAccessExpression>(asExpr.Right); // Quantity (type identifier)
    }
}
