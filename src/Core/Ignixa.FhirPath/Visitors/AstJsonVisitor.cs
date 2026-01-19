// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Visitor that serializes FhirPath expression AST to JSON format with inferred return types.
/// </summary>
/// <remarks>
/// This visitor creates a JSON representation of the AST that includes:
/// - ExpressionType: The type of expression node
/// - Name/Value: Node-specific data (property names, function names, constants, etc.)
/// - ReturnType: The inferred type from static analysis (if available)
/// - Location: Source position information
/// - Children/Arguments: Nested expression nodes
/// </remarks>
public sealed class AstJsonVisitor : IFhirPathExpressionVisitor<JsonSerializerOptions?, JsonObject>
{
    /// <summary>
    /// Serializes an expression tree to a JSON object.
    /// </summary>
    public static JsonObject Serialize(Expression expression, JsonSerializerOptions? options = null)
    {
        var visitor = new AstJsonVisitor();
        return expression.AcceptVisitor(visitor, options);
    }

    /// <summary>
    /// Serializes an expression tree to a JSON string.
    /// </summary>
    public static string SerializeToString(Expression expression, JsonSerializerOptions? options = null)
    {
        var jsonObj = Serialize(expression, options);
        return jsonObj.ToJsonString(options);
    }

    private JsonObject CreateBaseNode(Expression expression, string expressionType)
    {
        var node = new JsonObject
        {
            ["ExpressionType"] = expressionType
        };

        if (expression.InferredType != null)
        {
            node["ReturnType"] = expression.InferredType;
        }

        if (expression.Location != null)
        {
            node["Location"] = new JsonObject
            {
                ["Line"] = expression.Location.LineNumber,
                ["Column"] = expression.Location.LinePosition
            };
        }

        return node;
    }

    public JsonObject VisitScope(ScopeExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Scope");
        node["Name"] = expression.ScopeName;
        return node;
    }

    public JsonObject VisitBinary(BinaryExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Binary");
        node["Operator"] = expression.Operator.ToString();
        node["Left"] = expression.Left.AcceptVisitor(this, context);
        node["Right"] = expression.Right.AcceptVisitor(this, context);
        return node;
    }

    public JsonObject VisitUnary(UnaryExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Unary");
        node["Operator"] = expression.Operator.ToString();
        node["Operand"] = expression.Operand.AcceptVisitor(this, context);
        return node;
    }

    public JsonObject VisitFunctionCall(FunctionCallExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "FunctionCall");
        node["Name"] = expression.FunctionName;

        if (expression.Focus != null)
        {
            node["Focus"] = expression.Focus.AcceptVisitor(this, context);
        }

        if (expression.Arguments.Count > 0)
        {
            var args = new JsonArray();
            foreach (var arg in expression.Arguments)
            {
                args.Add(arg.AcceptVisitor(this, context));
            }
            node["Arguments"] = args;
        }

        return node;
    }

    public JsonObject VisitChild(ChildExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Child");
        node["Name"] = expression.ChildName;

        if (expression.Focus != null)
        {
            node["Focus"] = expression.Focus.AcceptVisitor(this, context);
        }

        return node;
    }

    public JsonObject VisitConstant(ConstantExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Constant");
        node["Value"] = JsonValue.Create(expression.Value);
        node["ValueType"] = expression.Value.GetType().Name;
        return node;
    }

    public JsonObject VisitIdentifier(IdentifierExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Identifier");
        node["Name"] = expression.Name;
        return node;
    }

    public JsonObject VisitVariable(VariableRefExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Variable");
        node["Name"] = expression.Name;
        return node;
    }

    public JsonObject VisitIndexer(IndexerExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Indexer");
        node["Collection"] = expression.Collection.AcceptVisitor(this, context);
        node["Index"] = expression.Index.AcceptVisitor(this, context);
        return node;
    }

    public JsonObject VisitParenthesized(ParenthesizedExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Parenthesized");
        node["Expression"] = expression.InnerExpression.AcceptVisitor(this, context);
        return node;
    }

    public JsonObject VisitQuantity(QuantityExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "Quantity");
        node["Value"] = expression.Value;
        node["Unit"] = expression.Unit;
        return node;
    }

    public JsonObject VisitEmpty(EmptyExpression expression, JsonSerializerOptions? context)
    {
        return CreateBaseNode(expression, "Empty");
    }

    public JsonObject VisitPropertyAccess(PropertyAccessExpression expression, JsonSerializerOptions? context)
    {
        var node = CreateBaseNode(expression, "PropertyAccess");
        node["Name"] = expression.PropertyName;

        if (expression.Focus != null)
        {
            node["Focus"] = expression.Focus.AcceptVisitor(this, context);
        }

        return node;
    }
}
