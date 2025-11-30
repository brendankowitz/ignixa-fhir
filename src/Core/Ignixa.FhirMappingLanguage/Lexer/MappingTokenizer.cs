/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language tokenizer using Superpower parser combinator library.
 * Based on FHIR StructureMap specification.
 */

using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Ignixa.FhirMappingLanguage.Lexer;

/// <summary>
/// Tokenizer for FHIR Mapping Language expressions using Superpower.
/// Provides two modes: standard (for evaluation) and with-trivia (for round-tripping).
/// </summary>
public static class MappingTokenizer
{
    /// <summary>
    /// Creates a tokenizer that preserves whitespace and comments for round-tripping.
    /// Use this when you need to reconstruct the original expression text.
    /// </summary>
    public static Tokenizer<MappingTokenKind> CreateWithTrivia()
    {
        return new TokenizerBuilder<MappingTokenKind>()
            // Comments (must come before other operators to avoid capturing // as division)
            .Match(Comment.CStyle, MappingTokenKind.BlockComment, requireDelimiters: true)
            .Match(Comment.CPlusPlusStyle, MappingTokenKind.LineComment)

            // Whitespace
            .Match(Span.WhiteSpace, MappingTokenKind.Whitespace)

            // Keywords (case-sensitive, must come before identifiers)
            .Match(Span.Regex(@"\bmap\b"), MappingTokenKind.Map, requireDelimiters: false)
            .Match(Span.Regex(@"\buses\b"), MappingTokenKind.Uses, requireDelimiters: false)
            .Match(Span.Regex(@"\bas\b"), MappingTokenKind.As, requireDelimiters: false)
            .Match(Span.Regex(@"\balias\b"), MappingTokenKind.Alias, requireDelimiters: false)
            .Match(Span.Regex(@"\bimports\b"), MappingTokenKind.Imports, requireDelimiters: false)
            .Match(Span.Regex(@"\bgroup\b"), MappingTokenKind.Group, requireDelimiters: false)
            .Match(Span.Regex(@"\bextends\b"), MappingTokenKind.Extends, requireDelimiters: false)
            .Match(Span.Regex(@"\bdefault\b"), MappingTokenKind.Default, requireDelimiters: false)
            .Match(Span.Regex(@"\bwhere\b"), MappingTokenKind.Where, requireDelimiters: false)
            .Match(Span.Regex(@"\bcheck\b"), MappingTokenKind.Check, requireDelimiters: false)
            .Match(Span.Regex(@"\blog\b"), MappingTokenKind.Log, requireDelimiters: false)
            .Match(Span.Regex(@"\bthen\b"), MappingTokenKind.Then, requireDelimiters: false)
            .Match(Span.Regex(@"\bsource\b"), MappingTokenKind.Source, requireDelimiters: false)
            .Match(Span.Regex(@"\btarget\b"), MappingTokenKind.Target, requireDelimiters: false)
            .Match(Span.Regex(@"\bqueried\b"), MappingTokenKind.Queried, requireDelimiters: false)
            .Match(Span.Regex(@"\bproduced\b"), MappingTokenKind.Produced, requireDelimiters: false)
            .Match(Span.Regex(@"\b[cC]oncept[mM]ap\b"), MappingTokenKind.ConceptMap, requireDelimiters: false)
            .Match(Span.Regex(@"\bprefix\b"), MappingTokenKind.Prefix, requireDelimiters: false)
            .Match(Span.Regex(@"\btypes\b"), MappingTokenKind.Types, requireDelimiters: false)
            .Match(Span.Regex(@"\btype\b"), MappingTokenKind.Type, requireDelimiters: false)
            .Match(Span.Regex(@"\bfirst\b"), MappingTokenKind.First, requireDelimiters: false)
            .Match(Span.Regex(@"\bnot_first\b"), MappingTokenKind.NotFirst, requireDelimiters: false)
            .Match(Span.Regex(@"\blast\b"), MappingTokenKind.Last, requireDelimiters: false)
            .Match(Span.Regex(@"\bnot_last\b"), MappingTokenKind.NotLast, requireDelimiters: false)
            .Match(Span.Regex(@"\bonly_one\b"), MappingTokenKind.OnlyOne, requireDelimiters: false)
            .Match(Span.Regex(@"\bshare\b"), MappingTokenKind.Share, requireDelimiters: false)
            .Match(Span.Regex(@"\bsingle\b"), MappingTokenKind.Single, requireDelimiters: false)
            .Match(Span.Regex(@"\bconstant\b"), MappingTokenKind.Constant, requireDelimiters: false)

            // Boolean literals
            .Match(Span.Regex(@"\btrue\b"), MappingTokenKind.True, requireDelimiters: false)
            .Match(Span.Regex(@"\bfalse\b"), MappingTokenKind.False, requireDelimiters: false)

            // String literals (single-quoted, SQL-style escaping)
            .Match(QuotedString.SqlStyle, MappingTokenKind.StringLiteral)

            // Delimited identifiers (backtick or double-quote style)
            .Match(Span.Regex("`[^`]*`"), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)
            .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)

            // Numeric literals
            .Match(Span.Regex(@"[0-9]+\.[0-9]+"), MappingTokenKind.DecimalLiteral, requireDelimiters: true)
            .Match(Span.Regex(@"[0-9]+"), MappingTokenKind.IntegerLiteral, requireDelimiters: true)

            // URLs (supports http://, https://, and urn: patterns)
            // More restrictive pattern to avoid matching prefix:code patterns like s:male
            .Match(Span.Regex(@"(https?|urn|ftp|file):[^\s<>""{}|\\^`\[\]]+"), MappingTokenKind.Url, requireDelimiters: false)

            // Identifiers (letters/underscore start, alphanumeric/underscore continuation)
            .Match(Identifier.CStyle, MappingTokenKind.Identifier)

            // Multi-character operators (longest first)
            .Match(Span.EqualTo("->"), MappingTokenKind.Arrow)
            .Match(Span.EqualTo(".."), MappingTokenKind.Range)
            .Match(Span.EqualTo("=="), MappingTokenKind.DoubleEquals)
            .Match(Span.EqualTo("~="), MappingTokenKind.RelatedTo)
            .Match(Span.EqualTo("!="), MappingTokenKind.NotEquals)
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            .Build();
    }

    /// <summary>
    /// Creates a tokenizer that ignores whitespace and comments for faster evaluation.
    /// Use this for standard expression execution where trivia is not needed.
    /// </summary>
    public static Tokenizer<MappingTokenKind> Create()
    {
        return new TokenizerBuilder<MappingTokenKind>()
            // Comments (ignore for standard parsing)
            .Ignore(Comment.CStyle)
            .Ignore(Comment.CPlusPlusStyle)

            // Keywords (case-sensitive, must come before identifiers)
            .Match(Span.Regex(@"\bmap\b"), MappingTokenKind.Map, requireDelimiters: false)
            .Match(Span.Regex(@"\buses\b"), MappingTokenKind.Uses, requireDelimiters: false)
            .Match(Span.Regex(@"\bas\b"), MappingTokenKind.As, requireDelimiters: false)
            .Match(Span.Regex(@"\balias\b"), MappingTokenKind.Alias, requireDelimiters: false)
            .Match(Span.Regex(@"\bimports\b"), MappingTokenKind.Imports, requireDelimiters: false)
            .Match(Span.Regex(@"\bgroup\b"), MappingTokenKind.Group, requireDelimiters: false)
            .Match(Span.Regex(@"\bextends\b"), MappingTokenKind.Extends, requireDelimiters: false)
            .Match(Span.Regex(@"\bdefault\b"), MappingTokenKind.Default, requireDelimiters: false)
            .Match(Span.Regex(@"\bwhere\b"), MappingTokenKind.Where, requireDelimiters: false)
            .Match(Span.Regex(@"\bcheck\b"), MappingTokenKind.Check, requireDelimiters: false)
            .Match(Span.Regex(@"\blog\b"), MappingTokenKind.Log, requireDelimiters: false)
            .Match(Span.Regex(@"\bthen\b"), MappingTokenKind.Then, requireDelimiters: false)
            .Match(Span.Regex(@"\bsource\b"), MappingTokenKind.Source, requireDelimiters: false)
            .Match(Span.Regex(@"\btarget\b"), MappingTokenKind.Target, requireDelimiters: false)
            .Match(Span.Regex(@"\bqueried\b"), MappingTokenKind.Queried, requireDelimiters: false)
            .Match(Span.Regex(@"\bproduced\b"), MappingTokenKind.Produced, requireDelimiters: false)
            .Match(Span.Regex(@"\b[cC]oncept[mM]ap\b"), MappingTokenKind.ConceptMap, requireDelimiters: false)
            .Match(Span.Regex(@"\bprefix\b"), MappingTokenKind.Prefix, requireDelimiters: false)
            .Match(Span.Regex(@"\btypes\b"), MappingTokenKind.Types, requireDelimiters: false)
            .Match(Span.Regex(@"\btype\b"), MappingTokenKind.Type, requireDelimiters: false)
            .Match(Span.Regex(@"\bfirst\b"), MappingTokenKind.First, requireDelimiters: false)
            .Match(Span.Regex(@"\bnot_first\b"), MappingTokenKind.NotFirst, requireDelimiters: false)
            .Match(Span.Regex(@"\blast\b"), MappingTokenKind.Last, requireDelimiters: false)
            .Match(Span.Regex(@"\bnot_last\b"), MappingTokenKind.NotLast, requireDelimiters: false)
            .Match(Span.Regex(@"\bonly_one\b"), MappingTokenKind.OnlyOne, requireDelimiters: false)
            .Match(Span.Regex(@"\bshare\b"), MappingTokenKind.Share, requireDelimiters: false)
            .Match(Span.Regex(@"\bsingle\b"), MappingTokenKind.Single, requireDelimiters: false)
            .Match(Span.Regex(@"\bconstant\b"), MappingTokenKind.Constant, requireDelimiters: false)

            // Boolean literals
            .Match(Span.Regex(@"\btrue\b"), MappingTokenKind.True, requireDelimiters: false)
            .Match(Span.Regex(@"\bfalse\b"), MappingTokenKind.False, requireDelimiters: false)

            // String literals (single-quoted, SQL-style escaping)
            .Match(QuotedString.SqlStyle, MappingTokenKind.StringLiteral)

            // Delimited identifiers (backtick or double-quote style)
            .Match(Span.Regex("`[^`]*`"), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)
            .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)

            // Numeric literals
            .Match(Span.Regex(@"[0-9]+\.[0-9]+"), MappingTokenKind.DecimalLiteral, requireDelimiters: true)
            .Match(Span.Regex(@"[0-9]+"), MappingTokenKind.IntegerLiteral, requireDelimiters: true)

            // URLs (supports http://, https://, and urn: patterns)
            // More restrictive pattern to avoid matching prefix:code patterns like s:male
            .Match(Span.Regex(@"(https?|urn|ftp|file):[^\s<>""{}|\\^`\[\]]+"), MappingTokenKind.Url, requireDelimiters: false)

            // Identifiers (letters/underscore start, alphanumeric/underscore continuation)
            .Match(Identifier.CStyle, MappingTokenKind.Identifier)

            // Multi-character operators (longest first)
            .Match(Span.EqualTo("->"), MappingTokenKind.Arrow)
            .Match(Span.EqualTo(".."), MappingTokenKind.Range)
            .Match(Span.EqualTo("=="), MappingTokenKind.DoubleEquals)
            .Match(Span.EqualTo("~="), MappingTokenKind.RelatedTo)
            .Match(Span.EqualTo("!="), MappingTokenKind.NotEquals)
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            // Whitespace (ignore for standard parsing)
            .Ignore(Span.WhiteSpace)

            .Build();
    }
}
