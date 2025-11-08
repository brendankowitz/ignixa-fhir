/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language grammar using Superpower token parser.
 * Based on FHIR StructureMap specification.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Lexer;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Parser grammar for FHIR Mapping Language.
/// Converts token streams into Expression abstract syntax trees.
/// </summary>
public static class MappingGrammar
{
    // Helper: Create position info from token
    private static ISourcePositionInfo CreatePosition(Token<MappingTokenKind> token) =>
        new MappingExpressionLocationInfo
        {
            LineNumber = token.Position.Line,
            LinePosition = token.Position.Column,
            RawPosition = (int)token.Position.Absolute,
            Length = token.Span.Length
        };

    // Helper: Create position info from span of tokens
    private static ISourcePositionInfo CreatePosition(Token<MappingTokenKind> start, Token<MappingTokenKind> end) =>
        new MappingExpressionLocationInfo
        {
            LineNumber = start.Position.Line,
            LinePosition = start.Position.Column,
            RawPosition = (int)start.Position.Absolute,
            Length = (int)(end.Position.Absolute - start.Position.Absolute) + end.Span.Length
        };

    // Helper: Unescape string
    private static string UnescapeString(string str)
    {
        if (str.StartsWith('\'') && str.EndsWith('\''))
        {
            str = str.Substring(1, str.Length - 2);
            str = str.Replace("''", "'", StringComparison.Ordinal);
        }
        return str;
    }

    // Helper: Unescape identifier
    private static string UnescapeIdentifier(string id)
    {
        if ((id.StartsWith('`') && id.EndsWith('`')) ||
            (id.StartsWith('"') && id.EndsWith('"')))
        {
            return id.Substring(1, id.Length - 2);
        }
        return id;
    }

    // Literal parsers
    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> StringLiteral =
        Token.EqualTo(MappingTokenKind.StringLiteral)
            .Select(t => new LiteralExpression(UnescapeString(t.ToStringValue()), CreatePosition(t)));

    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> IntegerLiteral =
        Token.EqualTo(MappingTokenKind.IntegerLiteral)
            .Select(t => new LiteralExpression(int.Parse(t.ToStringValue()), CreatePosition(t)));

    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> DecimalLiteral =
        Token.EqualTo(MappingTokenKind.DecimalLiteral)
            .Select(t => new LiteralExpression(decimal.Parse(t.ToStringValue()), CreatePosition(t)));

    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> BooleanLiteral =
        Token.EqualTo(MappingTokenKind.True)
            .Select(t => new LiteralExpression(true, CreatePosition(t)))
            .Or(Token.EqualTo(MappingTokenKind.False)
                .Select(t => new LiteralExpression(false, CreatePosition(t))));

    // Identifier parser - accepts keywords that can be used as identifiers
    private static readonly TokenListParser<MappingTokenKind, IdentifierExpression> Identifier =
        Token.EqualTo(MappingTokenKind.Identifier)
            .Or(Token.EqualTo(MappingTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(MappingTokenKind.Type))  // 'type' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Default))  // 'default' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Prefix))  // 'prefix' can be used as property name
            .Select(t => new IdentifierExpression(UnescapeIdentifier(t.ToStringValue()), CreatePosition(t)));

    // FHIRPath expression (embedded in parentheses or as unparenthesized expression)
    // Uses depth tracking to handle nested parentheses correctly
    private static readonly TokenListParser<MappingTokenKind, FhirPathExpression> FhirPathExpression =
        (TokenList<MappingTokenKind> input) =>
        {
            // Try to match opening paren
            var lparen = Token.EqualTo(MappingTokenKind.LeftParen)(input);

            if (lparen.HasValue)
            {
                // Parenthesized expression - collect until matching close paren
                var tokens = new List<Token<MappingTokenKind>>();
                var current = lparen.Remainder;
                var depth = 0;
                Token<MappingTokenKind> lastToken = lparen.Value;

                // Capture tokens until we find the matching closing paren
                while (!current.IsAtEnd)
                {
                    // Try to match a left paren
                    var leftParenResult = Token.EqualTo(MappingTokenKind.LeftParen)(current);
                    if (leftParenResult.HasValue)
                    {
                        depth++;
                        tokens.Add(leftParenResult.Value);
                        lastToken = leftParenResult.Value;
                        current = leftParenResult.Remainder;
                        continue;
                    }

                    // Try to match a right paren
                    var rightParenResult = Token.EqualTo(MappingTokenKind.RightParen)(current);
                    if (rightParenResult.HasValue)
                    {
                        if (depth == 0)
                        {
                            // Found matching closing paren - consume it and return
                            lastToken = rightParenResult.Value;
                            current = rightParenResult.Remainder;
                            var expr = new FhirPathExpression(
                                string.Join("", tokens.Select(t => t.ToStringValue())),
                                CreatePosition(lparen.Value, lastToken));
                            return TokenListParserResult.Value(expr, input, current);
                        }
                        else
                        {
                            depth--;
                            tokens.Add(rightParenResult.Value);
                            lastToken = rightParenResult.Value;
                            current = rightParenResult.Remainder;
                            continue;
                        }
                    }

                    // Match any other token
                    var anyToken = Token.Matching<MappingTokenKind>(_ => true, "any token")(current);
                    if (anyToken.HasValue)
                    {
                        tokens.Add(anyToken.Value);
                        lastToken = anyToken.Value;
                        current = anyToken.Remainder;
                    }
                    else
                    {
                        break;
                    }
                }

                return TokenListParserResult.Empty<MappingTokenKind, FhirPathExpression>(
                    input,
                    new[] { "unmatched parentheses in FHIRPath expression" });
            }
            else
            {
                // Non-parenthesized expression - collect until we hit a terminator
                // Terminators: keywords that end FHIRPath expression context
                var terminators = new HashSet<MappingTokenKind>
                {
                    MappingTokenKind.Arrow,        // -> starts targets
                    MappingTokenKind.Semicolon,    // ; ends rule
                    MappingTokenKind.RightBrace,   // } ends group
                    MappingTokenKind.Default,      // default starts new clause
                    MappingTokenKind.Where,        // where starts new clause
                    MappingTokenKind.Check,        // check starts new clause
                    MappingTokenKind.Log,          // log starts new clause
                    MappingTokenKind.Comma,        // , separates parameters/arguments
                };

                var tokens = new List<Token<MappingTokenKind>>();
                var current = input;
                Token<MappingTokenKind>? lastToken = null;
                var bracketDepth = 0;
                var parenDepth = 0;

                while (!current.IsAtEnd)
                {
                    var nextToken = Token.Matching<MappingTokenKind>(_ => true, "any token")(current);
                    if (!nextToken.HasValue)
                        break;

                    var tokenKind = nextToken.Value.Kind;

                    // Track bracket/paren depth
                    if (tokenKind == MappingTokenKind.LeftBracket)
                        bracketDepth++;
                    else if (tokenKind == MappingTokenKind.RightBracket)
                        bracketDepth--;
                    else if (tokenKind == MappingTokenKind.LeftParen)
                        parenDepth++;
                    else if (tokenKind == MappingTokenKind.RightParen)
                        parenDepth--;

                    // Check if we hit a terminator (only at depth 0)
                    if (bracketDepth == 0 && parenDepth == 0 && terminators.Contains(tokenKind))
                        break;

                    tokens.Add(nextToken.Value);
                    lastToken = nextToken.Value;
                    current = nextToken.Remainder;
                }

                if (tokens.Count == 0)
                    return TokenListParserResult.Empty<MappingTokenKind, FhirPathExpression>(input);

                var expr = new FhirPathExpression(
                    string.Join("", tokens.Select(t => t.ToStringValue())),
                    CreatePosition(tokens[0], lastToken!.Value));
                return TokenListParserResult.Value(expr, input, current);
            }
        };

    // Uses expression: uses "url" alias Name as source|target|queried|produced
    private static readonly TokenListParser<MappingTokenKind, UsesExpression> Uses =
        from usesToken in Token.EqualTo(MappingTokenKind.Uses)
        from url in StringLiteral
        from alias in (
            from aliasToken in Token.EqualTo(MappingTokenKind.Alias)
            from name in Identifier
            select name.Name
        ).OptionalOrDefault()
        from asToken in Token.EqualTo(MappingTokenKind.As)
        from mode in Token.EqualTo(MappingTokenKind.Source).Value(ModelMode.Source)
            .Or(Token.EqualTo(MappingTokenKind.Target).Value(ModelMode.Target))
            .Or(Token.EqualTo(MappingTokenKind.Queried).Value(ModelMode.Queried))
            .Or(Token.EqualTo(MappingTokenKind.Produced).Value(ModelMode.Produced))
        select new UsesExpression(
            url.Value.ToString()!,
            alias,
            mode,
            CreatePosition(usesToken));

    // Imports expression: imports "url"
    private static readonly TokenListParser<MappingTokenKind, ImportsExpression> Imports =
        from importsToken in Token.EqualTo(MappingTokenKind.Imports)
        from url in StringLiteral
        select new ImportsExpression(url.Value.ToString()!, CreatePosition(importsToken));

    // Parameter: source|target name : Type
    private static readonly TokenListParser<MappingTokenKind, ParameterExpression> Parameter =
        from mode in Token.EqualTo(MappingTokenKind.Source).Value(ParameterMode.Source)
            .Or(Token.EqualTo(MappingTokenKind.Target).Value(ParameterMode.Target))
        from name in Identifier
        from type in (
            from colon in Token.EqualTo(MappingTokenKind.Colon)
            from typeName in Identifier
            select typeName.Name
        ).OptionalOrDefault()
        select new ParameterExpression(mode, name.Name, type);

    // Qualified identifier: context.property[index] or just identifier
    // Supports chaining: src.name[0].given[1]
    private static readonly TokenListParser<MappingTokenKind, Expression> QualifiedIdentifier =
        from first in Identifier
        from rest in (
            // Either .property or [index]
            Token.EqualTo(MappingTokenKind.Dot)
                .IgnoreThen(Identifier)
                .Select(prop => (Func<Expression, Expression>)(acc => new QualifiedIdentifierExpression(acc, prop.Name)))
            .Or(Token.EqualTo(MappingTokenKind.LeftBracket)
                .IgnoreThen(Token.EqualTo(MappingTokenKind.IntegerLiteral))
                .Then(index => Token.EqualTo(MappingTokenKind.RightBracket)
                    .Value((Func<Expression, Expression>)(acc => new IndexExpression(acc, int.Parse(index.ToStringValue()))))))
        ).Many()
        select rest.Aggregate((Expression)first, (acc, func) => func(acc));

    // Transform argument expression (supports qualified identifiers and literals)
    private static readonly TokenListParser<MappingTokenKind, Expression> TransformArgumentExpression =
        StringLiteral.Select(l => (Expression)l)
            .Or(IntegerLiteral.Select(l => (Expression)l))
            .Or(DecimalLiteral.Select(l => (Expression)l))
            .Or(BooleanLiteral.Select(l => (Expression)l))
            .Or(QualifiedIdentifier);

    // Transform: functionName(arg1, arg2, ...)
    private static readonly TokenListParser<MappingTokenKind, TransformExpression> Transform =
        from name in Identifier
        from lparen in Token.EqualTo(MappingTokenKind.LeftParen)
        from args in TransformArgumentExpression
            .ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from rparen in Token.EqualTo(MappingTokenKind.RightParen)
        select new TransformExpression(name.Name, args, name.Location);

    // List mode
    private static readonly TokenListParser<MappingTokenKind, ListMode> ListModeParser =
        Token.EqualTo(MappingTokenKind.First).Value(ListMode.First)
            .Or(Token.EqualTo(MappingTokenKind.NotFirst).Value(ListMode.NotFirst))
            .Or(Token.EqualTo(MappingTokenKind.Last).Value(ListMode.Last))
            .Or(Token.EqualTo(MappingTokenKind.NotLast).Value(ListMode.NotLast))
            .Or(Token.EqualTo(MappingTokenKind.OnlyOne).Value(ListMode.OnlyOne))
            .Or(Token.EqualTo(MappingTokenKind.Share).Value(ListMode.Share))
            .Or(Token.EqualTo(MappingTokenKind.Single).Value(ListMode.Single));

    // Cardinality: min..max or min..*
    private static readonly TokenListParser<MappingTokenKind, Cardinality> CardinalityParser =
        from min in Token.EqualTo(MappingTokenKind.IntegerLiteral)
        from range in Token.EqualTo(MappingTokenKind.Range)
        from max in Token.EqualTo(MappingTokenKind.IntegerLiteral).Select(i => (int?)int.Parse(i.ToStringValue()))
                        .Or(Token.EqualTo(MappingTokenKind.Asterisk).Value((int?)null))
        select new Cardinality(int.Parse(min.ToStringValue()), max);

    // Source: context [: type] [as variable] [cardinality] [default value] [where condition] [check condition] [log message]
    // Note: Type constraint comes before 'as' variable per FHIR spec
    private static readonly TokenListParser<MappingTokenKind, SourceExpression> Source =
        from context in QualifiedIdentifier
        from type in (
            from colon in Token.EqualTo(MappingTokenKind.Colon)
            from typeName in Identifier
            select typeName.Name
        ).OptionalOrDefault()
        from variable in (
            from asToken in Token.EqualTo(MappingTokenKind.As)
            from varName in Identifier
            select varName.Name
        ).OptionalOrDefault()
        from cardinality in CardinalityParser.Select(c => (Cardinality?)c).OptionalOrDefault()
        from defaultValue in (
            from defaultToken in Token.EqualTo(MappingTokenKind.Default)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).OptionalOrDefault()
        from condition in (
            from whereToken in Token.EqualTo(MappingTokenKind.Where)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).OptionalOrDefault()
        from check in (
            from checkToken in Token.EqualTo(MappingTokenKind.Check)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).OptionalOrDefault()
        from log in (
            from logToken in Token.EqualTo(MappingTokenKind.Log)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).OptionalOrDefault()
        select new SourceExpression(
            context,
            variable,
            type,
            condition,
            check,
            log,
            defaultValue,
            cardinality);

    // Target: [context] [= expression] [as variable] [list mode]
    // Note: The order is context -> expression -> as variable -> list mode
    // This matches FHIR spec: "tgt.name = create('Type') as variable listmode"
    // The expression can be a literal value, a Transform (function call), or a variable reference
    // Note: Literals are tried first. Transform uses Try() to allow backtracking to QualifiedIdentifier
    private static readonly TokenListParser<MappingTokenKind, TargetExpression> Target =
        from context in QualifiedIdentifier.Select(e => (Expression?)e).OptionalOrDefault()
        from expression in (
            from equalsToken in Token.EqualTo(MappingTokenKind.Equals)
            from expr in StringLiteral.Select(l => (Expression)l)
                .Or(IntegerLiteral.Select(l => (Expression)l))
                .Or(DecimalLiteral.Select(l => (Expression)l))
                .Or(BooleanLiteral.Select(l => (Expression)l))
                .Or(Transform.Select(t => (Expression)t).Try())
                .Or(QualifiedIdentifier)
            select expr
        ).OptionalOrDefault()
        from variable in (
            from asToken in Token.EqualTo(MappingTokenKind.As)
            from varName in Identifier
            select varName.Name
        ).OptionalOrDefault()
        from listMode in ListModeParser.Optional()
        select new TargetExpression(
            context,
            variable,
            expression,
            listMode.HasValue ? listMode.Value : null);

    // Group invocation: GroupName(arg1, arg2, ...)
    private static readonly TokenListParser<MappingTokenKind, GroupInvocationExpression> GroupInvocation =
        from groupName in Identifier
        from lparen in Token.EqualTo(MappingTokenKind.LeftParen)
        from args in TransformArgumentExpression.ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from rparen in Token.EqualTo(MappingTokenKind.RightParen)
        select new GroupInvocationExpression(groupName.Name, args, groupName.Location);

    // Nested rules: { rule1; rule2; }
    private static readonly TokenListParser<MappingTokenKind, RuleSetExpression> NestedRules =
        from lbrace in Token.EqualTo(MappingTokenKind.LeftBrace)
#pragma warning disable CS8603 // Possible null reference return - Many() never returns null
        from rules in Parse.Ref(() => Rule).Many()
#pragma warning restore CS8603
        from rbrace in Token.EqualTo(MappingTokenKind.RightBrace)
        select new RuleSetExpression(rules, CreatePosition(lbrace));

    // Rule: source [, source]* [-> target [, target]*] [then (GroupInvocation | NestedRules)]
    // NOTE: Rule names (ruleName::) are not supported - not part of FHIR spec
    // FHIR spec uses trailing quoted strings for rule names instead
    // A rule MUST have at least one source (AtLeastOnceDelimitedBy prevents zero-width parser error)
    private static readonly TokenListParser<MappingTokenKind, RuleExpression> Rule =
        from sources in Source.AtLeastOnceDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from targets in (
            from arrow in Token.EqualTo(MappingTokenKind.Arrow)
            from tgts in Target.ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
            select tgts
        ).OptionalOrDefault()
        from dependent in (
            from thenToken in Token.EqualTo(MappingTokenKind.Then)
            from content in GroupInvocation.Select(g => (Expression)g).Or(NestedRules.Select(r => (Expression)r))
            select content
        ).OptionalOrDefault()
        from semicolon in Token.EqualTo(MappingTokenKind.Semicolon).Optional()
        select new RuleExpression(
            null, // Rule name not supported - see FHIR spec for trailing string syntax
            sources,
            targets ?? Array.Empty<TargetExpression>(),
            dependent);

    // Group: group Name(params) [extends OtherGroup] { rules }
    private static readonly TokenListParser<MappingTokenKind, GroupExpression> Group =
        from groupToken in Token.EqualTo(MappingTokenKind.Group)
        from name in Identifier
        from lparen in Token.EqualTo(MappingTokenKind.LeftParen)
        from parameters in Parameter.ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from rparen in Token.EqualTo(MappingTokenKind.RightParen)
        from extends in (
            from extendsToken in Token.EqualTo(MappingTokenKind.Extends)
            from extendName in Identifier
            select extendName.Name
        ).OptionalOrDefault()
        from lbrace in Token.EqualTo(MappingTokenKind.LeftBrace)
        from rules in Rule.Many()
        from rbrace in Token.EqualTo(MappingTokenKind.RightBrace)
        select new GroupExpression(
            name.Name,
            parameters,
            extends,
            rules,
            CreatePosition(groupToken));

    // Map: map "url" = "Identifier" [uses]* [imports]* [groups]*
    public static readonly TokenListParser<MappingTokenKind, MapExpression> Map =
        from mapToken in Token.EqualTo(MappingTokenKind.Map)
        from url in StringLiteral
        from equalsToken in Token.EqualTo(MappingTokenKind.Equals)
        from identifier in StringLiteral
        from uses in Uses.Many()
        from imports in Imports.Many()
        from groups in Group.Many()
        select new MapExpression(
            url.Value.ToString()!,
            identifier.Value.ToString()!,
            uses,
            imports,
            groups,
            CreatePosition(mapToken));
}
