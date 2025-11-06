/*
 * Copyright (c) 2025, Sparky Contributors
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

    // Identifier parser
    private static readonly TokenListParser<MappingTokenKind, IdentifierExpression> Identifier =
        Token.EqualTo(MappingTokenKind.Identifier)
            .Or(Token.EqualTo(MappingTokenKind.DelimitedIdentifier))
            .Select(t => new IdentifierExpression(UnescapeIdentifier(t.ToStringValue()), CreatePosition(t)));

    // FHIRPath expression (embedded in parentheses)
    private static readonly TokenListParser<MappingTokenKind, FhirPathExpression> FhirPathExpression =
        from lparen in Token.EqualTo(MappingTokenKind.LeftParen)
        from tokens in Token.EqualTo(MappingTokenKind.Identifier)
            .Or(Token.EqualTo(MappingTokenKind.Dot))
            .Or(Token.EqualTo(MappingTokenKind.LeftParen))
            .Or(Token.EqualTo(MappingTokenKind.RightParen))
            .Or(Token.EqualTo(MappingTokenKind.StringLiteral))
            .Many() // Simplified: just capture tokens until we balance parens
        from rparen in Token.EqualTo(MappingTokenKind.RightParen)
        select new FhirPathExpression(
            string.Join("", tokens.Select(t => t.ToStringValue())),
            CreatePosition(lparen, rparen));

    // Uses expression: uses "url" alias Name as source|target|queried|produced
    private static readonly TokenListParser<MappingTokenKind, UsesExpression> Uses =
        from usesToken in Token.EqualTo(MappingTokenKind.Uses)
        from url in StringLiteral
        from alias in (
            from aliasToken in Token.EqualTo(MappingTokenKind.Alias)
            from name in Identifier
            select name.Name
        ).Optional()
        from asToken in Token.EqualTo(MappingTokenKind.As)
        from mode in Token.EqualTo(MappingTokenKind.Source).Value(ModelMode.Source)
            .Or(Token.EqualTo(MappingTokenKind.Target).Value(ModelMode.Target))
            .Or(Token.EqualTo(MappingTokenKind.Queried).Value(ModelMode.Queried))
            .Or(Token.EqualTo(MappingTokenKind.Produced).Value(ModelMode.Produced))
        select new UsesExpression(
            url.Value.ToString()!,
            alias.HasValue ? alias.Value : null,
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
            from colon in Token.EqualTo(MappingTokenKind.DoubleColon)
            from typeName in Identifier
            select typeName.Name
        ).Optional()
        select new ParameterExpression(mode, name.Name, type.HasValue ? type.Value : null);

    // Primary expression (for context and values)
    private static readonly TokenListParser<MappingTokenKind, Expression> PrimaryExpression =
        StringLiteral.Select(l => (Expression)l)
            .Or(IntegerLiteral.Select(l => (Expression)l))
            .Or(DecimalLiteral.Select(l => (Expression)l))
            .Or(BooleanLiteral.Select(l => (Expression)l))
            .Or(Identifier.Select(i => (Expression)i));

    // Qualified identifier: context.property or just identifier
    private static readonly TokenListParser<MappingTokenKind, Expression> QualifiedIdentifier =
        from first in Identifier
        from rest in (
            from dot in Token.EqualTo(MappingTokenKind.Dot)
            from prop in Identifier
            select prop.Name
        ).Many()
        select rest.Aggregate((Expression)first, (acc, prop) =>
            new QualifiedIdentifierExpression(acc, prop));

    // Transform: functionName(arg1, arg2, ...)
    private static readonly TokenListParser<MappingTokenKind, TransformExpression> Transform =
        from name in Identifier
        from lparen in Token.EqualTo(MappingTokenKind.LeftParen)
        from args in PrimaryExpression
            .ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from rparen in Token.EqualTo(MappingTokenKind.RightParen)
        select new TransformExpression(name.Name, args, CreatePosition(name.Location as Token<MappingTokenKind> ?? default));

    // List mode
    private static readonly TokenListParser<MappingTokenKind, ListMode> ListModeParser =
        Token.EqualTo(MappingTokenKind.First).Value(ListMode.First)
            .Or(Token.EqualTo(MappingTokenKind.NotFirst).Value(ListMode.NotFirst))
            .Or(Token.EqualTo(MappingTokenKind.Last).Value(ListMode.Last))
            .Or(Token.EqualTo(MappingTokenKind.NotLast).Value(ListMode.NotLast))
            .Or(Token.EqualTo(MappingTokenKind.OnlyOne).Value(ListMode.OnlyOne))
            .Or(Token.EqualTo(MappingTokenKind.Share).Value(ListMode.Share))
            .Or(Token.EqualTo(MappingTokenKind.Single).Value(ListMode.Single));

    // Source: context [as variable] [: type] [default value] [where condition] [check condition] [log message]
    private static readonly TokenListParser<MappingTokenKind, SourceExpression> Source =
        from context in QualifiedIdentifier
        from variable in (
            from asToken in Token.EqualTo(MappingTokenKind.As)
            from varName in Identifier
            select varName.Name
        ).Optional()
        from type in (
            from colon in Token.EqualTo(MappingTokenKind.DoubleColon)
            from typeName in Identifier
            select typeName.Name
        ).Optional()
        from defaultValue in (
            from defaultToken in Token.EqualTo(MappingTokenKind.Default)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).Optional()
        from condition in (
            from whereToken in Token.EqualTo(MappingTokenKind.Where)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).Optional()
        from check in (
            from checkToken in Token.EqualTo(MappingTokenKind.Check)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).Optional()
        from log in (
            from logToken in Token.EqualTo(MappingTokenKind.Log)
            from expr in Parse.Ref(() => FhirPathExpression)
            select expr
        ).Optional()
        select new SourceExpression(
            context,
            variable.HasValue ? variable.Value : null,
            type.HasValue ? type.Value : null,
            condition.HasValue ? condition.Value : null,
            check.HasValue ? check.Value : null,
            log.HasValue ? log.Value : null,
            defaultValue.HasValue ? defaultValue.Value : null);

    // Target: [context] [as variable] [= transform] [list mode]
    private static readonly TokenListParser<MappingTokenKind, TargetExpression> Target =
        from context in QualifiedIdentifier.Optional()
        from variable in (
            from asToken in Token.EqualTo(MappingTokenKind.As)
            from varName in Identifier
            select varName.Name
        ).Optional()
        from transform in (
            from equals in Token.EqualTo(MappingTokenKind.Equals)
            from trans in Transform
            select trans
        ).Optional()
        from listMode in ListModeParser.Optional()
        select new TargetExpression(
            context.HasValue ? context.Value : null,
            variable.HasValue ? variable.Value : null,
            transform.HasValue ? transform.Value : null,
            listMode.HasValue ? listMode.Value : null);

    // Rule: [name:] source [, source]* [-> target [, target]*] [then { rule* }]
    private static readonly TokenListParser<MappingTokenKind, RuleExpression> Rule =
        from name in (
            from id in Identifier
            from colon in Token.EqualTo(MappingTokenKind.DoubleColon)
            select id.Name
        ).Optional()
        from sources in Source.ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
        from targets in (
            from arrow in Token.EqualTo(MappingTokenKind.Arrow)
            from tgts in Target.ManyDelimitedBy(Token.EqualTo(MappingTokenKind.Comma))
            select tgts
        ).Optional()
        from dependent in (
            from thenToken in Token.EqualTo(MappingTokenKind.Then)
            from lbrace in Token.EqualTo(MappingTokenKind.LeftBrace)
            from rules in Parse.Ref(() => Rule).Many()
            from rbrace in Token.EqualTo(MappingTokenKind.RightBrace)
            select rules
        ).Optional()
        from semicolon in Token.EqualTo(MappingTokenKind.Semicolon).Optional()
        select new RuleExpression(
            name.HasValue ? name.Value : null,
            sources,
            targets.HasValue ? targets.Value : Array.Empty<TargetExpression>(),
            dependent.HasValue ? dependent.Value : Array.Empty<RuleExpression>());

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
        ).Optional()
        from lbrace in Token.EqualTo(MappingTokenKind.LeftBrace)
        from rules in Rule.Many()
        from rbrace in Token.EqualTo(MappingTokenKind.RightBrace)
        select new GroupExpression(
            name.Name,
            parameters,
            extends.HasValue ? extends.Value : null,
            rules,
            CreatePosition(groupToken));

    // Map: map "url" = "Identifier" [uses]* [imports]* [groups]*
    public static readonly TokenListParser<MappingTokenKind, MapExpression> Map =
        from mapToken in Token.EqualTo(MappingTokenKind.Map)
        from url in StringLiteral
        from equals in Token.EqualTo(MappingTokenKind.Equals)
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
