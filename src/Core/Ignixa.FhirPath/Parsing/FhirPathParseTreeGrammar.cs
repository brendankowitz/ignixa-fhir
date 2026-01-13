/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath grammar producing parse trees (not AST).
 * Based on FhirPath N1.0 (Normative) specification.
 */

using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing.ParseTree;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// Parser grammar for FhirPath expressions producing parse trees.
/// Parse trees are then converted to AST via visitor pattern.
/// </summary>
internal static class FhirPathParseTreeGrammar
{
    private static SourceLocation Loc(Token<FhirPathTokenKind> token) =>
        SourceLocation.From(token);

    private static SourceLocation Loc(Token<FhirPathTokenKind> start, Token<FhirPathTokenKind> end) =>
        SourceLocation.From(start, end);

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> StringLiteral =
        Token.EqualTo(FhirPathTokenKind.StringLiteral)
            .Select(t => new ConstantParseNode(UnescapeString(t.ToStringValue()), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> IntegerLiteral =
        Token.EqualTo(FhirPathTokenKind.IntegerLiteral)
            .Select(t => new ConstantParseNode(int.Parse(t.ToStringValue()), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> LongLiteral =
        Token.EqualTo(FhirPathTokenKind.LongLiteral)
            .Select(t =>
            {
                var value = t.ToStringValue();
                // Remove the 'L' or 'l' suffix before parsing
                var numericPart = value.Substring(0, value.Length - 1);
                return new ConstantParseNode(long.Parse(numericPart), Loc(t));
            });

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> DecimalLiteral =
        Token.EqualTo(FhirPathTokenKind.DecimalLiteral)
            .Select(t => new ConstantParseNode(decimal.Parse(t.ToStringValue()), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> BooleanLiteral =
        Token.EqualTo(FhirPathTokenKind.BooleanLiteral)
            .Select(t => new ConstantParseNode(bool.Parse(t.ToStringValue()), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> DateLiteral =
        Token.EqualTo(FhirPathTokenKind.DateLiteral)
            .Select(t => new ConstantParseNode(t.ToStringValue(), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> DateTimeLiteral =
        Token.EqualTo(FhirPathTokenKind.DateTimeLiteral)
            .Select(t => new ConstantParseNode(t.ToStringValue(), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ConstantParseNode> TimeLiteral =
        Token.EqualTo(FhirPathTokenKind.TimeLiteral)
            .Select(t => new ConstantParseNode(t.ToStringValue(), Loc(t)));

    public static readonly TokenListParser<FhirPathTokenKind, QuantityParseNode> Quantity =
        from valueToken in Token.EqualTo(FhirPathTokenKind.DecimalLiteral)
            .Or(Token.EqualTo(FhirPathTokenKind.IntegerLiteral))
        from unitToken in Token.EqualTo(FhirPathTokenKind.StringLiteral)
        select new QuantityParseNode(
            decimal.Parse(valueToken.ToStringValue()),
            UnescapeString(unitToken.ToStringValue()),
            Loc(valueToken, unitToken));

    private static readonly TokenListParser<FhirPathTokenKind, QuantityParseNode> CalendarDuration =
        from valueToken in Token.EqualTo(FhirPathTokenKind.DecimalLiteral)
            .Or(Token.EqualTo(FhirPathTokenKind.IntegerLiteral))
        from keywordToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Where(t => Types.CalendarDuration.IsCalendarKeyword(t.ToStringValue()))
        select new QuantityParseNode(
            decimal.Parse(valueToken.ToStringValue()),
            keywordToken.ToStringValue(),  // Preserve keyword form (week, year, etc.) for toString()
            Loc(valueToken, keywordToken));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> Literal =
        CalendarDuration.Select(q => (ParseNode)q).Try()
            .Or(Quantity.Select(q => (ParseNode)q).Try())
            .Or(StringLiteral.Select(c => (ParseNode)c))
            .Or(DateTimeLiteral.Select(c => (ParseNode)c))
            .Or(TimeLiteral.Select(c => (ParseNode)c))
            .Or(DateLiteral.Select(c => (ParseNode)c))
            .Or(BooleanLiteral.Select(c => (ParseNode)c))
            .Or(DecimalLiteral.Select(c => (ParseNode)c))
            .Or(LongLiteral.Select(c => (ParseNode)c))
            .Or(IntegerLiteral.Select(c => (ParseNode)c));

    private static readonly TokenListParser<FhirPathTokenKind, ScopeParseNode> Axis =
        Token.EqualTo(FhirPathTokenKind.Axis)
            .Select(t => new ScopeParseNode(t.ToStringValue().Substring(1), Loc(t)));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> ExternalConstant =
        Token.EqualTo(FhirPathTokenKind.ExternalConstant)
            .Select(t =>
            {
                var raw = t.ToStringValue().Substring(1); // Remove '%'
                // Check if delimited with backticks and remove them
                var varName = (raw.Length >= 2 && raw[0] == '`' && raw[raw.Length - 1] == '`')
                    ? raw.Substring(1, raw.Length - 2)
                    : raw;
                return (ParseNode)new VariableRefParseNode(varName, Loc(t));
            });

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> ParenthesizedExpression =
        from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
        from expr in Parse.Ref(() => Expression!)
        from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
        select (ParseNode)new ParenthesizedParseNode(expr, Loc(lparen, rparen));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> EmptyCollection =
        from lbrace in Token.EqualTo(FhirPathTokenKind.LeftBrace)
        from rbrace in Token.EqualTo(FhirPathTokenKind.RightBrace)
        select (ParseNode)new EmptyParseNode(Loc(lbrace, rbrace));

    // Instance selector: TypeName { element: value, element: value, ... }
    // or empty initializer: TypeName {:}
    private static TokenListParser<FhirPathTokenKind, ParseNode> InstanceSelector() =>
        from typeTokens in Token.EqualTo(FhirPathTokenKind.Identifier)
            .ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Dot))
        let typeIdentifiers = typeTokens.ToArray()
        from lbrace in Token.EqualTo(FhirPathTokenKind.LeftBrace)
        from elements in (
            // Check for empty initializer {:}
            from colon in Token.EqualTo(FhirPathTokenKind.Colon)
            from rbrace in Token.EqualTo(FhirPathTokenKind.RightBrace)
            select (elements: Array.Empty<ElementAssignmentParseNode>(), isEmpty: true, rbrace)
        ).Or(
            // Parse element assignments
            from assignments in (
                from elementName in Token.EqualTo(FhirPathTokenKind.Identifier)
                from colon in Token.EqualTo(FhirPathTokenKind.Colon)
                from valueExpr in Parse.Ref(() => Expression!)
                select new ElementAssignmentParseNode(
                    UnescapeIdentifier(elementName.ToStringValue()),
                    valueExpr,
                    Loc(elementName))
            ).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
            from rbrace in Token.EqualTo(FhirPathTokenKind.RightBrace)
            select (elements: assignments.ToArray(), isEmpty: false, rbrace)
        ).Or(
            // Empty object without colon: {} (already parsed by EmptyCollection, but handle here too)
            from rbrace in Token.EqualTo(FhirPathTokenKind.RightBrace)
            select (elements: Array.Empty<ElementAssignmentParseNode>(), isEmpty: false, rbrace)
        )
        select (ParseNode)new InstanceSelectorParseNode(
            typeIdentifiers.Length == 1
                ? UnescapeIdentifier(typeIdentifiers[0].ToStringValue())
                : UnescapeIdentifier(typeIdentifiers[typeIdentifiers.Length - 1].ToStringValue()),
            typeIdentifiers.Length > 1
                ? string.Join(".", typeIdentifiers.Take(typeIdentifiers.Length - 1).Select(t => UnescapeIdentifier(t.ToStringValue())))
                : null,
            elements.elements,
            elements.isEmpty,
            Loc(typeIdentifiers[0], elements.rbrace));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> QualifiedIdentifier =
        from parts in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .AtLeastOnceDelimitedBy(Token.EqualTo(FhirPathTokenKind.Dot))
        select (ParseNode)new IdentifierParseNode(
            string.Join(".", parts.Select(t => UnescapeIdentifier(t.ToStringValue()))),
            Loc(parts.First(), parts.Last()));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> TypeSpecifierArgument =
        QualifiedIdentifier;

    private static bool IsTypeSpecifierFunction(string functionName)
    {
        var unescaped = UnescapeIdentifier(functionName);
        return unescaped.Equals("oftype", StringComparison.OrdinalIgnoreCase) ||
               unescaped.Equals("as", StringComparison.OrdinalIgnoreCase) ||
               unescaped.Equals("is", StringComparison.OrdinalIgnoreCase);
    }

    private static TokenListParser<FhirPathTokenKind, ParseNode> IdentifierOrFunction() =>
        from nameToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(FhirPathTokenKind.Contains))
            .Or(Token.EqualTo(FhirPathTokenKind.As))
            .Or(Token.EqualTo(FhirPathTokenKind.Is))
            .Or(Token.EqualTo(FhirPathTokenKind.In))
        from maybeArgs in (
            from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
            from args in (IsTypeSpecifierFunction(nameToken.ToStringValue())
                ? TypeSpecifierArgument.ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
                : Parse.Ref(() => Expression!).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma)))
            from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
            select (args, rparen)
        ).Optional()
        select maybeArgs.HasValue
            ? (ParseNode)new FunctionCallParseNode(
                new ScopeParseNode("that", default),
                UnescapeIdentifier(nameToken.ToStringValue()),
                maybeArgs.Value.args.ToArray(),
                Loc(nameToken, maybeArgs.Value.rparen))
            : (ParseNode)new PropertyAccessParseNode(
                null,
                UnescapeIdentifier(nameToken.ToStringValue()),
                Loc(nameToken));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> Term =
        Literal
            .Or(Axis.Select(a => (ParseNode)a))
            .Or(ExternalConstant)
            .Or(ParenthesizedExpression)
            .Or(InstanceSelector().Try()) // Try instance selector before identifier (needs lookahead for '{')
            .Or(EmptyCollection)
            .Or(IdentifierOrFunction());

    private static readonly TokenListParser<FhirPathTokenKind, Func<ParseNode, ParseNode>> DotInvocation =
        from dot in Token.EqualTo(FhirPathTokenKind.Dot)
        from nameToken in Token.EqualTo(FhirPathTokenKind.Identifier)
            .Or(Token.EqualTo(FhirPathTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(FhirPathTokenKind.Contains))
            .Or(Token.EqualTo(FhirPathTokenKind.As))
            .Or(Token.EqualTo(FhirPathTokenKind.Is))
            .Or(Token.EqualTo(FhirPathTokenKind.In))
        from maybeFunction in (
            from lparen in Token.EqualTo(FhirPathTokenKind.LeftParen)
            from args in (IsTypeSpecifierFunction(nameToken.ToStringValue())
                ? TypeSpecifierArgument.ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma))
                : Parse.Ref(() => Expression!).ManyDelimitedBy(Token.EqualTo(FhirPathTokenKind.Comma)))
            from rparen in Token.EqualTo(FhirPathTokenKind.RightParen)
            select (args, rparen)
        ).Optional()
        select new Func<ParseNode, ParseNode>(focus =>
            maybeFunction.HasValue
                ? new FunctionCallParseNode(
                    focus,
                    UnescapeIdentifier(nameToken.ToStringValue()),
                    maybeFunction.Value.args.ToArray(),
                    Loc(nameToken, maybeFunction.Value.rparen))
                : new ChildParseNode(
                    focus,
                    UnescapeIdentifier(nameToken.ToStringValue()),
                    Loc(nameToken)));

    private static readonly TokenListParser<FhirPathTokenKind, Func<ParseNode, ParseNode>> IndexerInvocation =
        from lbracket in Token.EqualTo(FhirPathTokenKind.LeftBracket)
        from index in Parse.Ref(() => Expression!)
        from rbracket in Token.EqualTo(FhirPathTokenKind.RightBracket)
        select new Func<ParseNode, ParseNode>(focus =>
            new IndexerParseNode(focus, index, Loc(lbracket, rbracket)));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> InvocationExpression =
        from initial in Term
        from invocations in DotInvocation.Try().Or(IndexerInvocation.Try()).Many()
        select invocations.Aggregate(initial, (current, invoke) => invoke(current));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> PolarityExpression =
        Token.EqualTo(FhirPathTokenKind.Plus)
            .Or(Token.EqualTo(FhirPathTokenKind.Minus))
            .Optional()
            .Then(op => InvocationExpression.Select(expr =>
                op.HasValue
                    ? (ParseNode)new UnaryParseNode(op.Value.ToStringValue(), expr, Loc(op.Value))
                    : expr));

    private static TokenListParser<FhirPathTokenKind, ParseNode> BinaryOp(
        TokenListParser<FhirPathTokenKind, Token<FhirPathTokenKind>> opParser,
        TokenListParser<FhirPathTokenKind, ParseNode> operandParser) =>
        Parse.Chain(opParser, operandParser, (op, left, right) =>
            new BinaryParseNode(left, op.ToStringValue(), right, Loc(op)));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> MultiplicativeExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Multiply)
                .Or(Token.EqualTo(FhirPathTokenKind.Divide))
                .Or(Token.EqualTo(FhirPathTokenKind.Div))
                .Or(Token.EqualTo(FhirPathTokenKind.Mod)),
            PolarityExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> AdditiveExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Plus)
                .Or(Token.EqualTo(FhirPathTokenKind.Minus))
                .Or(Token.EqualTo(FhirPathTokenKind.Ampersand)),
            MultiplicativeExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> UnionExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Union),
            AdditiveExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> InequalityExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.LessThan)
                .Or(Token.EqualTo(FhirPathTokenKind.LessThanOrEqual))
                .Or(Token.EqualTo(FhirPathTokenKind.GreaterThan))
                .Or(Token.EqualTo(FhirPathTokenKind.GreaterThanOrEqual)),
            UnionExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> TypeExpression =
        InequalityExpression.Then(left =>
            Token.EqualTo(FhirPathTokenKind.Is)
                .Or(Token.EqualTo(FhirPathTokenKind.As))
                .Then(op => TypeSpecifierArgument.Select(typeName => (op, typeName)))
                .Optional()
                .Select(typeOp =>
                    typeOp.HasValue
                        ? (ParseNode)new BinaryParseNode(
                            left,
                            typeOp.Value.op.ToStringValue(),
                            typeOp.Value.typeName,
                            Loc(typeOp.Value.op))
                        : left));

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> EqualityExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Equals)
                .Or(Token.EqualTo(FhirPathTokenKind.NotEquals))
                .Or(Token.EqualTo(FhirPathTokenKind.Equivalent))
                .Or(Token.EqualTo(FhirPathTokenKind.NotEquivalent)),
            TypeExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> MembershipExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.In)
                .Or(Token.EqualTo(FhirPathTokenKind.Contains)),
            EqualityExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> AndExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.And),
            MembershipExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> OrExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Or)
                .Or(Token.EqualTo(FhirPathTokenKind.Xor)),
            AndExpression);

    private static readonly TokenListParser<FhirPathTokenKind, ParseNode> ImpliesExpression =
        BinaryOp(
            Token.EqualTo(FhirPathTokenKind.Implies),
            OrExpression);

    public static readonly TokenListParser<FhirPathTokenKind, ParseNode> Expression = ImpliesExpression;

    private static string UnescapeString(string str)
    {
        // Remove quotes if present
        if (str.StartsWith('\'') && str.EndsWith('\'') && str.Length >= 2)
        {
            str = str.Substring(1, str.Length - 2);
        }

        // Handle SQL-style escaping first (for backward compatibility)
        str = str.Replace("''", "'", StringComparison.Ordinal);

        // Handle backslash escapes
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < str.Length; )
        {
            if (str[i] == '\\' && i + 1 < str.Length)
            {
                char next = str[i + 1];
                switch (next)
                {
                    case '\'':
                        sb.Append('\'');
                        i += 2; // Skip both \ and '
                        break;
                    case '"':
                        sb.Append('"');
                        i += 2; // Skip both \ and "
                        break;
                    case '`':
                        sb.Append('`');
                        i += 2; // Skip both \ and `
                        break;
                    case '/':
                        sb.Append('/');
                        i += 2; // Skip both \ and /
                        break;
                    case '\\':
                        sb.Append('\\');
                        i += 2; // Skip both backslashes
                        break;
                    case 'r':
                        sb.Append('\r');
                        i += 2; // Skip \ and r
                        break;
                    case 'n':
                        sb.Append('\n');
                        i += 2; // Skip \ and n
                        break;
                    case 't':
                        sb.Append('\t');
                        i += 2; // Skip \ and t
                        break;
                    case 'f':
                        sb.Append('\f');
                        i += 2; // Skip \ and f
                        break;
                    case 'u':
                        if (i + 5 < str.Length)
                        {
                            var hex = str.Substring(i + 2, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int codePoint))
                            {
                                sb.Append(char.ConvertFromUtf32(codePoint));
                                i += 6; // Skip \, u, and 4 hex digits
                            }
                            else
                            {
                                sb.Append(str[i]); // Invalid hex, keep backslash
                                i++;
                            }
                        }
                        else
                        {
                            sb.Append(str[i]); // Incomplete escape, keep backslash
                            i++;
                        }
                        break;
                    default:
                        sb.Append(str[i]); // Unknown escape, keep backslash
                        i++;
                        break;
                }
            }
            else
            {
                sb.Append(str[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static string UnescapeIdentifier(string id)
    {
        if ((id.StartsWith('`') && id.EndsWith('`')) ||
            (id.StartsWith('"') && id.EndsWith('"')))
        {
            return id.Substring(1, id.Length - 2);
        }
        return id;
    }
}
