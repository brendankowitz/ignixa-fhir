/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * FhirPath grammar using Superpower token parser.
 * Based on FhirPath N1.0 (Normative) specification.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Lexer;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Ignixa.FhirPath.Parser;

/// <summary>
/// Parser grammar for FhirPath expressions.
/// Converts token streams into Expression abstract syntax trees.
/// </summary>
public static class FhirPathGrammar
{
    // Helper: Create position info from token
    private static ISourcePositionInfo CreatePosition(Token<FhirPathTokenKind> token) =>
        new FhirPathExpressionLocationInfo
        {
            LineNumber = token.Position.Line,
            LinePosition = token.Position.Column,
            RawPosition = (int)token.Position.Absolute,
            Length = token.Span.Length
        };

    // Helper: Create position info from span of tokens
    private static ISourcePositionInfo CreatePosition(Token<FhirPathTokenKind> start, Token<FhirPathTokenKind> end) =>
        new FhirPathExpressionLocationInfo
        {
            LineNumber = start.Position.Line,
            LinePosition = start.Position.Column,
            RawPosition = (int)start.Position.Absolute,
            Length = (int)(end.Position.Absolute - start.Position.Absolute) + end.Span.Length
        };

    // Literal parsers
    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> StringLiteral =
        Token.EqualTo(FhirPathTokenKind.StringLiteral)
            .Select(t => new ConstantExpression(
                UnescapeString(t.ToStringValue()),
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> IntegerLiteral =
        Token.EqualTo(FhirPathTokenKind.IntegerLiteral)
            .Select(t => new ConstantExpression(
                int.Parse(t.ToStringValue()),
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> DecimalLiteral =
        Token.EqualTo(FhirPathTokenKind.DecimalLiteral)
            .Select(t => new ConstantExpression(
                decimal.Parse(t.ToStringValue()),
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> BooleanLiteral =
        Token.EqualTo(FhirPathTokenKind.BooleanLiteral)
            .Select(t => new ConstantExpression(
                bool.Parse(t.ToStringValue()),
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> DateLiteral =
        Token.EqualTo(FhirPathTokenKind.DateLiteral)
            .Select(t => new ConstantExpression(
                t.ToStringValue(), // Store as string for now
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> DateTimeLiteral =
        Token.EqualTo(FhirPathTokenKind.DateTimeLiteral)
            .Select(t => new ConstantExpression(
                t.ToStringValue(), // Store as string for now
                CreatePosition(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantExpression> TimeLiteral =
        Token.EqualTo(FhirPathTokenKind.TimeLiteral)
            .Select(t => new ConstantExpression(
                t.ToStringValue(), // Store as string for now
                CreatePosition(t)));

    // Literal: any constant value
    private static readonly TokenListParser<FhirPathTokenKind, Expression> Literal =
        StringLiteral
            .Or(DateTimeLiteral)
            .Or(TimeLiteral)
            .Or(DateLiteral)
            .Or(BooleanLiteral)
            .Or(DecimalLiteral)
            .Or(IntegerLiteral)
            .Select(c => (Expression)c);

    // Quantity: number followed by unit string (e.g., 5 'mg', 37.5 'Cel')
    // Note: This is a separate public parser not integrated into Literal due to backtracking limitations.
    // Quantities can be parsed explicitly in contexts where they are expected.
    public static readonly TokenListParser<FhirPathTokenKind, QuantityExpression> Quantity =
        from valueToken in Token.EqualTo(FhirPathTokenKind.DecimalLiteral)
            .Or(Token.EqualTo(FhirPathTokenKind.IntegerLiteral))
        from unitToken in Token.EqualTo(FhirPathTokenKind.StringLiteral)
        select new QuantityExpression(
            decimal.Parse(valueToken.ToStringValue()),
            UnescapeString(unitToken.ToStringValue()),
            CreatePosition(valueToken, unitToken));


    // Axis: $this, $index, $total
    private static readonly TokenListParser<FhirPathTokenKind, AxisExpression> Axis =
        Token.EqualTo(FhirPathTokenKind.Axis)
            .Select(t =>
            {
                var axisName = t.ToStringValue().Substring(1); // Remove '$'
                return new AxisExpression(axisName, CreatePosition(t));
            });

    // External constant: %identifier
    private static readonly TokenListParser<FhirPathTokenKind, Expression> ExternalConstant =
        Token.EqualTo(FhirPathTokenKind.ExternalConstant)
            .Select(t => (Expression)new VariableRefExpression(
                t.ToStringValue().Substring(1), // Remove '%'
                CreatePosition(t)));

    // Parenthesized expression: (expression)
    private static readonly TokenListParser<FhirPathTokenKind, Expression> ParenthesizedExpression =
        from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
        from expr in Parse.Ref(() => Expression!)
        from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
        select (Expression)new ParenthesizedExpression(expr, CreatePosition(lparen, rparen));

    // Empty collection: {}
    private static readonly TokenListParser<FhirPathTokenKind, Expression> EmptyCollection =
        from lbrace in Token.EqualTo(FhirPathTokenKind.LeftBrace)
        from rbrace in Token.EqualTo(FhirPathTokenKind.RightBrace)
        select (Expression)new EmptyExpression(CreatePosition(lbrace, rbrace));

    // Type specifier: bare identifier (used in ofType() arguments)
    // Per FHIRPath spec, type specifiers are identifiers that represent type names
    private static readonly TokenListParser<FhirPathTokenKind, Expression> TypeSpecifierArgument =
        Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Select(t => (Expression)new IdentifierExpression(
                UnescapeIdentifier(t.ToStringValue()),
                CreatePosition(t)));

    // Helper: identifier that could be function(args) or bare identifier
    // Bare identifiers at root level are treated as function calls (e.g., "Patient" = "Patient()")
    // NOTE: Keywords can be used as function/property names in certain contexts
    // SPECIAL CASE: ofType() arguments are type specifiers, not expressions
    private static TokenListParser<FhirPathTokenKind, Expression> IdentifierOrFunction() =>
        from nameToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(FhirPathTokenKind.Contains)) // Allow "contains" as function/property name
            .Or(Token.EqualTo(FhirPathTokenKind.As)) // Allow "as" as function/property name
            .Or(Token.EqualTo(FhirPathTokenKind.Is)) // Allow "is" as function/property name
            .Or(Token.EqualTo(FhirPathTokenKind.In)) // Allow "in" as function/property name
        from maybeArgs in (
            from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
            from args in (UnescapeIdentifier(nameToken.ToStringValue()).Equals("oftype", StringComparison.OrdinalIgnoreCase)
                ? TypeSpecifierArgument.ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
                : Parse.Ref(() => Expression!).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma)))
            from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
            select (args, rparen)
        ).Optional()
        select (Expression)new FunctionCallExpression(
            AxisExpression.That,
            UnescapeIdentifier(nameToken.ToStringValue()),
            maybeArgs.HasValue ? maybeArgs.Value.args : Array.Empty<Expression>(),
            maybeArgs.HasValue ? CreatePosition(nameToken, maybeArgs.Value.rparen) : CreatePosition(nameToken));

    // Function call: identifier(args) - used by DotInvocation
    // NOTE: Keywords can be used as function names in certain contexts
    // SPECIAL CASE: ofType() arguments are type specifiers, not expressions
    private static TokenListParser<FhirPathTokenKind, FunctionCallExpression> FunctionCall(Expression? focus) =>
        from nameToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(FhirPathTokenKind.Contains)) // Allow "contains" as function name
            .Or(Token.EqualTo(FhirPathTokenKind.As)) // Allow "as" as function name
            .Or(Token.EqualTo(FhirPathTokenKind.Is)) // Allow "is" as function name
            .Or(Token.EqualTo(FhirPathTokenKind.In)) // Allow "in" as function name
        from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
        from args in (UnescapeIdentifier(nameToken.ToStringValue()).Equals("oftype", StringComparison.OrdinalIgnoreCase)
            ? TypeSpecifierArgument.ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
            : Parse.Ref(() => Expression!).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma)))
        from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
        select new FunctionCallExpression(
            focus,
            UnescapeIdentifier(nameToken.ToStringValue()),
            args,
            CreatePosition(nameToken, rparen));

    // Term: literal | axis | variable | (expr) | {} | identifier/function
    private static readonly TokenListParser<FhirPathTokenKind, Expression> Term =
        Literal
            .Or(Axis.Select(a => (Expression)a))
            .Or(ExternalConstant)
            .Or(ParenthesizedExpression)
            .Or(EmptyCollection)
            .Or(IdentifierOrFunction());

    // Dot invocation: .identifier or .function()
    // NOTE: Keywords can be used as property/function names after dot (e.g., .contains, .as, .is, .in)
    // SPECIAL CASE: ofType() arguments are type specifiers, not expressions
    private static readonly TokenListParser<FhirPathTokenKind, Func<Expression, Expression>> DotInvocation =
        from dot in Token.EqualTo(FhirPathTokenKind.Dot)
        from nameToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(FhirPathTokenKind.Contains)) // Allow "contains" as property/function name
            .Or(Token.EqualTo(FhirPathTokenKind.As)) // Allow "as" as property/function name
            .Or(Token.EqualTo(FhirPathTokenKind.Is)) // Allow "is" as property/function name
            .Or(Token.EqualTo(FhirPathTokenKind.In)) // Allow "in" as property/function name
        from maybeFunction in (
            from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
            from args in (UnescapeIdentifier(nameToken.ToStringValue()).Equals("oftype", StringComparison.OrdinalIgnoreCase)
                ? TypeSpecifierArgument.ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
                : Parse.Ref(() => Expression!).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma)))
            from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
            select (args, rparen)
        ).Optional()
        select new Func<Expression, Expression>(focus =>
            maybeFunction.HasValue
                ? new FunctionCallExpression(
                    focus,
                    UnescapeIdentifier(nameToken.ToStringValue()),
                    maybeFunction.Value.args,
                    CreatePosition(nameToken, maybeFunction.Value.rparen))
                : new ChildExpression(
                    focus,
                    UnescapeIdentifier(nameToken.ToStringValue()),
                    CreatePosition(nameToken)));

    // Indexer: [expression]
    private static readonly TokenListParser<FhirPathTokenKind, Func<Expression, Expression>> IndexerInvocation =
        from lbracket in Token.EqualTo(FhirPathTokenKind.LeftBracket)
        from index in Parse.Ref(() => Expression!)
        from rbracket in Token.EqualTo(FhirPathTokenKind.RightBracket)
        select new Func<Expression, Expression>(focus =>
            new IndexerExpression(focus, index, CreatePosition(lbracket, rbracket)));

    // Invocation: term (.member | [index])*
    private static readonly TokenListParser<FhirPathTokenKind, Expression> InvocationExpression =
        from initial in Term
        from invocations in DotInvocation.Or(IndexerInvocation).Many()
        select invocations.Aggregate(initial, (current, invoke) => invoke(current));

    // Unary: (+|-) invocation
    private static readonly TokenListParser<FhirPathTokenKind, Expression> PolarityExpression =
        Token.EqualTo(FhirPathTokenKind.Plus)
            .Or(Token.EqualTo(FhirPathTokenKind.Minus))
            .Optional()
            .Then(op => InvocationExpression.Select(expr =>
                op.HasValue
                    ? (Expression)new UnaryExpression(op.Value.ToStringValue(), expr, CreatePosition(op.Value))
                    : expr));

    // Binary operators with precedence
    private static TokenListParser<FhirPathTokenKind, Expression> BinaryOp(
        TokenListParser<FhirPathTokenKind, Token<FhirPathTokenKind>> opParser,
        TokenListParser<FhirPathTokenKind, Expression> operandParser) =>
        Parse.Chain(opParser, operandParser, (op, left, right) =>
            new BinaryExpression(op.ToStringValue(), left, right, CreatePosition(op)));

    // Multiplicative: * / div mod
    private static readonly TokenListParser<FhirPathTokenKind, Expression> MultiplicativeExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Multiply)
                .Or(Token.EqualTo(FhirPathTokenKind.Divide))
                .Or(Token.EqualTo(FhirPathTokenKind.Div))
                .Or(Token.EqualTo(FhirPathTokenKind.Mod)),
            PolarityExpression);

    // Additive: + - &
    private static readonly TokenListParser<FhirPathTokenKind, Expression> AdditiveExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Plus)
                .Or(Token.EqualTo(FhirPathTokenKind.Minus))
                .Or(Token.EqualTo(FhirPathTokenKind.Ampersand)),
            MultiplicativeExpression);

    // Union: |
    private static readonly TokenListParser<FhirPathTokenKind, Expression> UnionExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Union),
            AdditiveExpression);

    // Inequality: < <= > >=
    private static readonly TokenListParser<FhirPathTokenKind, Expression> InequalityExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.LessThan)
                .Or(Token.EqualTo(FhirPathTokenKind.LessThanOrEqual))
                .Or(Token.EqualTo(FhirPathTokenKind.GreaterThan))
                .Or(Token.EqualTo(FhirPathTokenKind.GreaterThanOrEqual)),
            UnionExpression);

    // Type operators: is as
    // NOTE: Type name on right side should be treated as function call (e.g., "Quantity" = "Quantity()")
    private static readonly TokenListParser<FhirPathTokenKind, Expression> TypeExpression =
        InequalityExpression.Then(left =>
            Token.EqualTo(FhirPathTokenKind.Is)
                .Or(Token.EqualTo(FhirPathTokenKind.As))
                .Then(op => IdentifierOrFunction().Select(typeName => (op, typeName)))
                .Optional()
                .Select(typeOp =>
                    typeOp.HasValue
                        ? (Expression)new BinaryExpression(
                            typeOp.Value.op.ToStringValue(),
                            left,
                            typeOp.Value.typeName,
                            CreatePosition(typeOp.Value.op))
                        : left));

    // Equality: = != ~ !~
    private static readonly TokenListParser<FhirPathTokenKind, Expression> EqualityExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Equals)
                .Or(Token.EqualTo(FhirPathTokenKind.NotEquals))
                .Or(Token.EqualTo(FhirPathTokenKind.Equivalent))
                .Or(Token.EqualTo(FhirPathTokenKind.NotEquivalent)),
            TypeExpression);

    // Membership: in contains
    private static readonly TokenListParser<FhirPathTokenKind, Expression> MembershipExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.In)
                .Or(Token.EqualTo(FhirPathTokenKind.Contains)),
            EqualityExpression);

    // Logical AND: and
    private static readonly TokenListParser<FhirPathTokenKind, Expression> AndExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.And),
            MembershipExpression);

    // Logical OR/XOR: or xor
    private static readonly TokenListParser<FhirPathTokenKind, Expression> OrExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Or)
                .Or(Token.EqualTo(FhirPathTokenKind.Xor)),
            AndExpression);

    // Implies: implies
    private static readonly TokenListParser<FhirPathTokenKind, Expression> ImpliesExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Implies),
            OrExpression);

    // Top-level expression
    public static readonly TokenListParser<FhirPathTokenKind, Expression> Expression = ImpliesExpression;

    // Helper methods
    private static string UnescapeString(string str)
    {
        // Remove quotes and unescape
        if (str.StartsWith('\'') && str.EndsWith('\''))
        {
            str = str.Substring(1, str.Length - 2);
            str = str.Replace("''", "'", StringComparison.Ordinal); // SQL-style escaping
        }
        return str;
    }

    private static string UnescapeIdentifier(string id)
    {
        // Remove quotes/backticks for delimited identifiers
        if ((id.StartsWith('`') && id.EndsWith('`')) ||
            (id.StartsWith('"') && id.EndsWith('"')))
        {
            return id.Substring(1, id.Length - 2);
        }
        return id;
    }
}
