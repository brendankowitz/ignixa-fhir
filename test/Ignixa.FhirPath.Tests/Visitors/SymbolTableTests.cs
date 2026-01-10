// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Visitors;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Visitors;

public class SymbolTableTests
{
    [Fact]
    public void GivenSymbolTable_WhenInitialized_ThenContainsStandardFunctions()
    {
        // Arrange & Act
        var symbolTable = new SymbolTable();

        // Assert
        Assert.True(symbolTable.FunctionCount > 50);

        // Verify some core functions are registered
        Assert.NotNull(symbolTable.Get("where"));
        Assert.NotNull(symbolTable.Get("select"));
        Assert.NotNull(symbolTable.Get("first"));
        Assert.NotNull(symbolTable.Get("last"));
        Assert.NotNull(symbolTable.Get("count"));
        Assert.NotNull(symbolTable.Get("exists"));
        Assert.NotNull(symbolTable.Get("empty"));
        Assert.NotNull(symbolTable.Get("all"));
        Assert.NotNull(symbolTable.Get("any"));
    }

    [Fact]
    public void GivenSymbolTable_WhenLookingUpFunction_ThenReturnsDefinition()
    {
        // Arrange
        var symbolTable = new SymbolTable();

        // Act
        var whereFunc = symbolTable.Get("where");

        // Assert
        Assert.NotNull(whereFunc);
        Assert.Equal("where", whereFunc.Name);
        Assert.True(whereFunc.SupportsCollections);
    }

    [Fact]
    public void GivenSymbolTable_WhenLookingUpNonExistentFunction_ThenReturnsNull()
    {
        // Arrange
        var symbolTable = new SymbolTable();

        // Act
        var result = symbolTable.Get("nonExistentFunction");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenCustomFunction_WhenAddedToSymbolTable_ThenCanBeLookedUp()
    {
        // Arrange
        var symbolTable = new SymbolTable();
        var customFunc = new FunctionDefinition("customFunc", supportsCollections: true, supportedAtRoot: false);

        // Act
        symbolTable.Add(customFunc);
        var retrieved = symbolTable.Get("customFunc");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("customFunc", retrieved.Name);
        Assert.True(retrieved.SupportsCollections);
    }

    [Fact]
    public void GivenFunctionNames_WhenQuerying_ThenReturnsAllRegistered()
    {
        // Arrange
        var symbolTable = new SymbolTable();

        // Act
        var names = symbolTable.FunctionNames.ToList();

        // Assert
        Assert.NotEmpty(names);
        Assert.Contains("where", names);
        Assert.Contains("select", names);
        Assert.Contains("count", names);
        Assert.Contains("exists", names);
    }

    [Fact]
    public void GivenFunctionDefinition_WhenAddingContexts_ThenContextsAreRegistered()
    {
        // Arrange
        var func = new FunctionDefinition("testFunc");

        // Act
        func.AddContexts("string-integer, decimal-integer");

        // Assert
        Assert.Equal(2, func.SupportedContexts.Count);
        Assert.Equal("string", func.SupportedContexts[0].ContextType);
        Assert.Equal("integer", func.SupportedContexts[0].ReturnType);
        Assert.Equal("decimal", func.SupportedContexts[1].ContextType);
        Assert.Equal("integer", func.SupportedContexts[1].ReturnType);
    }

    [Fact]
    public void GivenFunctionDefinition_WhenCheckingReturnType_ThenReturnsCorrectType()
    {
        // Arrange
        var func = new FunctionDefinition("testFunc");
        func.AddContexts("string-integer");

        // Act
        var returnType = func.GetReturnTypeForContext("string");

        // Assert
        Assert.Equal("integer", returnType);
    }

    [Fact]
    public void GivenValidateArgumentCount_WhenTooFewArguments_ThenAddsError()
    {
        // Arrange
        var parser = new FhirPathParser();
        var expr = parser.Parse("(1 | 2).take()");
        var functionCall = FindFunctionCall(expr, "take");
        Assert.NotNull(functionCall);

        var definition = new FunctionDefinition("take");
        var validation = SymbolTable.ValidateArgumentCount(min: 1, max: 1);
        var issues = new List<ValidationIssue>();

        // Act
        validation(functionCall, definition, Array.Empty<FhirPathTypeSet>(), issues);

        // Assert
        Assert.NotEmpty(issues);
        Assert.Equal(ValidationIssueSeverity.Error, issues[0].Severity);
        Assert.Contains("at least 1 argument", issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenValidateArgumentCount_WhenTooManyArguments_ThenAddsError()
    {
        // Arrange
        var parser = new FhirPathParser();
        var expr = parser.Parse("empty()");
        var functionCall = FindFunctionCall(expr, "empty");
        Assert.NotNull(functionCall);

        var definition = new FunctionDefinition("empty");
        var validation = SymbolTable.ValidateArgumentCount(min: 0, max: 0);
        var issues = new List<ValidationIssue>();
        var args = new[] { new FhirPathTypeSet(), new FhirPathTypeSet() };

        // Act
        validation(functionCall, definition, args, issues);

        // Assert
        Assert.NotEmpty(issues);
        Assert.Equal(ValidationIssueSeverity.Error, issues[0].Severity);
        Assert.Contains("at most 0 argument", issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenReturnsContext_WhenCalled_ThenReturnsFocusTypes()
    {
        // Arrange
        var definition = new FunctionDefinition("where");
        var focus = new FhirPathTypeSet();
        focus.AddPrimitiveType("string");
        var issues = new List<ValidationIssue>();

        // Act
        var result = SymbolTable.ReturnsContext(definition, focus, Array.Empty<FhirPathTypeSet>(), issues);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal("string", result[0].TypeName);
    }

    [Fact]
    public void GivenReturnsFromArgument_WhenNoArguments_ThenReturnsFocusTypes()
    {
        // Arrange
        var definition = new FunctionDefinition("select");
        var focus = new FhirPathTypeSet();
        focus.AddPrimitiveType("Patient");
        var issues = new List<ValidationIssue>();

        // Act
        var result = SymbolTable.ReturnsFromArgument(definition, focus, Array.Empty<FhirPathTypeSet>(), issues);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal("Patient", result[0].TypeName);
    }

    [Fact]
    public void GivenReturnsFromArgument_WhenHasArgument_ThenReturnsArgumentTypes()
    {
        // Arrange
        var definition = new FunctionDefinition("select");
        var focus = new FhirPathTypeSet();
        focus.AddPrimitiveType("Patient", forceCollection: true);

        var argumentProps = new FhirPathTypeSet();
        argumentProps.AddPrimitiveType("string");

        var issues = new List<ValidationIssue>();

        // Act
        var result = SymbolTable.ReturnsFromArgument(definition, focus, new[] { argumentProps }, issues);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal("string", result[0].TypeName);
        Assert.True(result[0].IsCollection); // Inherits collection from focus
    }

    private static FunctionCallExpression? FindFunctionCall(Expression expr, string functionName)
    {
        if (expr is FunctionCallExpression func && func.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase))
        {
            return func;
        }

        // Recursively search child expressions
        if (expr is BinaryExpression binary)
        {
            return FindFunctionCall(binary.Left, functionName) ?? FindFunctionCall(binary.Right, functionName);
        }
        if (expr is UnaryExpression unary)
        {
            return FindFunctionCall(unary.Operand, functionName);
        }
        if (expr is FunctionCallExpression funcExpr)
        {
            if (funcExpr.Focus != null)
            {
                var found = FindFunctionCall(funcExpr.Focus, functionName);
                if (found != null) return found;
            }
            foreach (var arg in funcExpr.Arguments)
            {
                var found = FindFunctionCall(arg, functionName);
                if (found != null) return found;
            }
        }

        return null;
    }
}
