// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Analysis;

/// <summary>
/// Finds the FHIRPath System types an expression may construct for static cast analysis.
/// </summary>
/// <remarks>
/// This analysis is intentionally an over-approximation. Any expression shape or function metadata that
/// cannot prove a finite set must return <see cref="SystemTypeConstruction.MayConstructAny"/>. A false
/// <c>AlwaysEmpty</c> result can reject a valid expression, while a missed diagnostic is benign; do not
/// tighten an unknown case without proving that the evaluator cannot construct a System value there.
/// Namespace provenance belongs to the expression that constructs a value, not to inferred type identity.
/// </remarks>
internal sealed class SystemTypeConstructionAnalyzer(
    SymbolTable symbolTable,
    Func<string, bool> isKnownRootPropertyName)
{
    public SystemTypeConstruction Analyze(Expression? expression) =>
        expression switch
        {
            null => SystemTypeConstruction.Any,
            BinaryExpression binary => AnalyzeBinary(binary),
            ConstantExpression constant => SystemTypeConstruction.For(GetConstantTypeName(constant)),
            QuantityExpression => SystemTypeConstruction.For("Quantity"),
            ParenthesizedExpression parenthesized => Analyze(parenthesized.InnerExpression),
            UnaryExpression unary => AnalyzeUnary(unary),
            PropertyAccessExpression property => AnalyzePropertyAccess(property),
            ChildExpression child => AnalyzeChild(child),
            IndexerExpression => SystemTypeConstruction.Any,
            FunctionCallExpression function => AnalyzeFunction(function),
            EmptyExpression => SystemTypeConstruction.Empty,
            InstanceSelectorExpression => SystemTypeConstruction.None,
            VariableRefExpression => SystemTypeConstruction.Any,
            ScopeExpression => SystemTypeConstruction.Any,
            IdentifierExpression => SystemTypeConstruction.Any,
            _ => SystemTypeConstruction.Any,
        };

    /// <summary>
    /// Treats a child navigated off a constructed System value as an unknown System-type construction.
    /// </summary>
    /// <remarks>
    /// The evaluator projects System value members such as <c>Quantity.value</c> and <c>Quantity.unit</c>,
    /// so a child whose focus constructs a System value constructs one too. Naming that member's type would
    /// require mirroring the evaluator's member map, which cannot be proven complete, so the negative answer
    /// survives only when the focus is proven to construct nothing. <see cref="AnalyzePropertyAccess"/>
    /// applies the same test: today the grammar produces a property access only at term position, so its
    /// focus is always null, but the node's constructor is public and nothing enforces that.
    /// Because the rule over-approximates, a uniform sweep of expression shapes measures a large apparent
    /// loss of true always-empty diagnostics - 7,696 of 18,240 rows at <c>8780aaf1..d090c8a9</c>, measured
    /// 2026-08-20 - but that ratio is an artefact of weighting synthetic foci equally with real ones. Over
    /// the shipped search parameter corpus the delta is 0 of 8,827 parameter/base-resource pairs across all
    /// five versions, because no shipped search parameter navigates off a constructed System value. Do not
    /// read that ratio as a regression and tighten this arm back into asserting a negative without
    /// consulting the focus; that is the unsoundness it exists to remove. A System type member map
    /// (<c>Quantity</c> alone has members) would recover precision on the synthetic rows, and is the option
    /// to revisit only if a future population, such as tenant search parameters or FHIRPath drawn from
    /// invariants, is found to contain quantity-rooted navigation.
    /// </remarks>
    private SystemTypeConstruction AnalyzeChild(ChildExpression expression)
    {
        var focus = Analyze(expression.Focus);
        return focus.MayConstructAny || focus.TypeNames.Count > 0
            ? SystemTypeConstruction.Any
            : SystemTypeConstruction.None;
    }

    private SystemTypeConstruction AnalyzePropertyAccess(PropertyAccessExpression expression)
    {
        if (expression.Focus is null)
        {
            return isKnownRootPropertyName(expression.PropertyName)
                ? SystemTypeConstruction.None
                : SystemTypeConstruction.Any;
        }

        var focus = Analyze(expression.Focus);
        return focus.MayConstructAny || focus.TypeNames.Count > 0
            ? SystemTypeConstruction.Any
            : SystemTypeConstruction.None;
    }

    /// <summary>
    /// Names the FHIRPath type a constant constructs.
    /// </summary>
    /// <remarks>
    /// A temporal literal is recognised by its node kind, not by its value. Sniffing a leading <c>@</c>
    /// classified the string literal <c>'@x'</c> as a date, because the grammar hands both literal kinds
    /// the same CLR string; that made <c>'@'.length()</c> a hard error and <c>'@x' as String</c> a
    /// confident always-empty verdict on expressions the evaluator answers. Email-shaped literals appear
    /// throughout FHIR invariants, so the misclassification is reachable.
    /// </remarks>
    public static string GetConstantTypeName(ConstantExpression expression) =>
        expression is TemporalConstantExpression temporal
            ? temporal.TemporalTypeName
            : GetValueTypeName(expression.Value);

    /// <summary>
    /// Names the FHIRPath type a constant's CLR payload constructs, independent of the literal's node kind.
    /// </summary>
    internal static string GetValueTypeName(object? value) =>
        value switch
        {
            null => "empty",
            bool => "boolean",
            int or long => "integer",
            decimal or double or float => "decimal",
            string => "string",
            DateTime or DateTimeOffset => "dateTime",
            _ => "string",
        };

    private SystemTypeConstruction AnalyzeBinary(BinaryExpression expression) =>
        expression.Operator switch
        {
            "|" => Analyze(expression.Left).Union(Analyze(expression.Right)),
            "+" or "-" or "*" or "/" or "div" or "mod" => AnalyzeArithmetic(expression),
            "=" or "!=" or "~" or "!~" or "<" or ">" or "<=" or ">=" or
                "and" or "or" or "xor" or "implies" or "in" or "contains" or "is" =>
                SystemTypeConstruction.For("boolean"),
            "&" => SystemTypeConstruction.For("string"),
            "as" => Analyze(expression.Left),
            _ => SystemTypeConstruction.Any,
        };

    /// <summary>
    /// Treats a non-empty arithmetic result as an unknown constructed System value.
    /// </summary>
    /// <remarks>
    /// Arithmetic constructs a new value whose type is not necessarily either operand's type: notably,
    /// adding two navigated FHIR strings constructs a System string. Enumerating today's result matrix
    /// would turn future or overlooked evaluator cases into false always-empty diagnostics, so only the
    /// evaluator's invariant that an empty operand yields an empty result is modeled precisely.
    /// </remarks>
    private SystemTypeConstruction AnalyzeArithmetic(BinaryExpression expression)
    {
        var left = Analyze(expression.Left);
        var right = Analyze(expression.Right);
        return left.IsKnownEmpty || right.IsKnownEmpty
            ? SystemTypeConstruction.Empty
            : SystemTypeConstruction.Any;
    }

    private SystemTypeConstruction AnalyzeUnary(UnaryExpression expression)
    {
        if (expression.Operator == "not")
        {
            return SystemTypeConstruction.For("boolean");
        }

        if (expression.Operator == "-" && TryGetLongConstant(expression.Operand, out long value))
        {
            if (value == int.MinValue)
            {
                return SystemTypeConstruction.Empty;
            }

            return value is >= int.MinValue and <= int.MaxValue
                ? SystemTypeConstruction.For("integer")
                : SystemTypeConstruction.For("decimal");
        }

        var operand = Analyze(expression.Operand);
        if (expression.Operator == "+" || operand.IsKnownEmpty)
        {
            return operand;
        }

        return expression.Operator == "-"
            ? operand.Negate()
            : operand;
    }

    /// <summary>
    /// Preserves the evaluator's value-dependent result type for negated long literals.
    /// </summary>
    /// <remarks>
    /// Long constants use the runtime type name <c>integer</c>, but negation narrows in-range values to
    /// Integer and promotes wider values to Decimal. Reading the literal value avoids broadening ordinary
    /// integer negation, which would discard valid always-empty diagnostics for mismatched Decimal casts.
    /// </remarks>
    private static bool TryGetLongConstant(Expression expression, out long value)
    {
        if (expression is ParenthesizedExpression parenthesized)
        {
            return TryGetLongConstant(parenthesized.InnerExpression, out value);
        }

        if (expression is ConstantExpression { Value: long constant })
        {
            value = constant;
            return true;
        }

        value = default;
        return false;
    }

    private SystemTypeConstruction AnalyzeFunction(FunctionCallExpression expression)
    {
        var definition = symbolTable.Get(expression.FunctionName);
        if (definition is null)
        {
            return SystemTypeConstruction.Any;
        }

        string returnType = definition.DeclaredReturnType;
        if (returnType.Equals("context", StringComparison.OrdinalIgnoreCase))
        {
            return Analyze(expression.Focus);
        }

        if (returnType.Equals("constructsFromContext", StringComparison.OrdinalIgnoreCase) ||
            returnType.Equals("boundaryOfContext", StringComparison.OrdinalIgnoreCase))
        {
            return AnalyzeConstructionFromContext(expression);
        }

        if (returnType.Equals("fromArgument", StringComparison.OrdinalIgnoreCase))
        {
            return expression.FunctionName.Equals("iif", StringComparison.OrdinalIgnoreCase)
                ? AnalyzeIif(expression)
                : AnalyzeArguments(expression.Arguments);
        }

        if (returnType.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return SystemTypeConstruction.Any;
        }

        string? runtimeTypeName = GetSystemPrimitiveRuntimeTypeName(returnType);
        if (runtimeTypeName is not null)
        {
            return SystemTypeConstruction.For(runtimeTypeName);
        }

        return symbolTable.IsKnownFhirType(returnType)
            ? SystemTypeConstruction.None
            : SystemTypeConstruction.Any;
    }

    /// <summary>
    /// Reports a function that builds a new value out of its focus as constructing an unnamed System type.
    /// </summary>
    /// <remarks>
    /// These functions return a freshly built primitive element rather than an element selected out of the
    /// focus, so the focus's namespace provenance does not survive them and inheriting it would report a
    /// valid System-spelled cast as provably empty from R5 onward. Naming the constructed type would mean
    /// enumerating the evaluator's result matrix per function, which cannot be proven complete, so this
    /// stays an over-approximation. Only the evaluator's empty-in/empty-out invariant is carried through:
    /// a focus that is provably empty produces a provably empty result.
    /// </remarks>
    private SystemTypeConstruction AnalyzeConstructionFromContext(FunctionCallExpression expression) =>
        Analyze(expression.Focus).IsKnownEmpty
            ? SystemTypeConstruction.Empty
            : SystemTypeConstruction.Any;

    private SystemTypeConstruction AnalyzeArguments(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count == 0)
        {
            return SystemTypeConstruction.Any;
        }

        var result = SystemTypeConstruction.None;
        foreach (var argument in arguments)
        {
            result = result.Union(Analyze(argument));
        }

        return result;
    }

    private SystemTypeConstruction AnalyzeIif(FunctionCallExpression expression)
    {
        if (expression.Arguments.Count < 2)
        {
            return SystemTypeConstruction.Any;
        }

        if (expression.Arguments[0] is ConstantExpression { Value: bool condition })
        {
            if (condition)
            {
                return Analyze(expression.Arguments[1]);
            }

            return expression.Arguments.Count > 2
                ? Analyze(expression.Arguments[2])
                : SystemTypeConstruction.Empty;
        }

        var result = Analyze(expression.Arguments[1]);
        return expression.Arguments.Count > 2
            ? result.Union(Analyze(expression.Arguments[2]))
            : result;
    }

    private static string? GetSystemPrimitiveRuntimeTypeName(string typeName) =>
        typeName.ToUpperInvariant() switch
        {
            "BOOLEAN" => "boolean",
            "INTEGER" => "integer",
            "LONG" => "long",
            "STRING" => "string",
            "DECIMAL" => "decimal",
            "URI" => "uri",
            "URL" => "url",
            "CANONICAL" => "canonical",
            "BASE64BINARY" => "base64Binary",
            "INSTANT" => "instant",
            "DATE" => "date",
            "DATETIME" => "dateTime",
            "TIME" => "time",
            "CODE" => "code",
            "OID" => "oid",
            "ID" => "id",
            "MARKDOWN" => "markdown",
            "UNSIGNEDINT" => "unsignedInt",
            "POSITIVEINT" => "positiveInt",
            "UUID" => "uuid",
            "XHTML" => "xhtml",
            "QUANTITY" => "Quantity",
            _ => null,
        };

}
