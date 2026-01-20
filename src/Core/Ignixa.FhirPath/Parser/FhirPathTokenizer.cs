/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath tokenizer using Superpower parser combinator library.
 * Based on FhirPath N1.0 (Normative) specification.
 */

using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Ignixa.FhirPath.Parser;

/// <summary>
/// Tokenizer for FhirPath expressions using Superpower.
/// Provides two modes: standard (for evaluation) and with-trivia (for round-tripping).
/// </summary>
public static class FhirPathTokenizer
{
        /// <summary>
        /// Creates a tokenizer that preserves whitespace and comments for round-tripping.
        /// Use this when you need to reconstruct the original expression text.
        /// </summary>
        public static Tokenizer<FhirPathTokenKind> CreateWithTrivia()
        {
            return new TokenizerBuilder<FhirPathTokenKind>()
                // Comments (must come before whitespace to avoid capturing // as division + division)
                .Match(Comment.CStyle, FhirPathTokenKind.BlockComment, requireDelimiters: true)
                .Match(Comment.CPlusPlusStyle, FhirPathTokenKind.LineComment)

                // Whitespace
                .Match(Span.WhiteSpace, FhirPathTokenKind.Whitespace)

                // Keywords (must come before identifiers, case-sensitive per FHIRPath N1.0 spec, require word boundary)
                // Use regex with \b word boundaries to prevent matching within identifiers (e.g., "is" in "issued")
                .Match(Span.Regex(@"\band\b"), FhirPathTokenKind.And, requireDelimiters: false)
                .Match(Span.Regex(@"\bor\b"), FhirPathTokenKind.Or, requireDelimiters: false)
                .Match(Span.Regex(@"\bxor\b"), FhirPathTokenKind.Xor, requireDelimiters: false)
                .Match(Span.Regex(@"\bimplies\b"), FhirPathTokenKind.Implies, requireDelimiters: false)
                .Match(Span.Regex(@"\bis\b"), FhirPathTokenKind.Is, requireDelimiters: false)
                .Match(Span.Regex(@"\bas\b"), FhirPathTokenKind.As, requireDelimiters: false)
                .Match(Span.Regex(@"\bdiv\b"), FhirPathTokenKind.Div, requireDelimiters: false)
                .Match(Span.Regex(@"\bmod\b"), FhirPathTokenKind.Mod, requireDelimiters: false)
                .Match(Span.Regex(@"\bin\b"), FhirPathTokenKind.In, requireDelimiters: false)
                .Match(Span.Regex(@"\bcontains\b"), FhirPathTokenKind.Contains, requireDelimiters: false)

                // Boolean literals (keywords, case-sensitive per spec)
                .Match(Span.Regex(@"\btrue\b"), FhirPathTokenKind.BooleanLiteral, requireDelimiters: false)
                .Match(Span.Regex(@"\bfalse\b"), FhirPathTokenKind.BooleanLiteral, requireDelimiters: false)

                // DateTime literals (must have 'T' to distinguish from Date)
                // Full DateTime: @YYYY-MM-DDTHH:MM:SS.FFF(Z|±HH:MM)?
                .Match(Span.Regex(@"@[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?(Z|[+-][0-9]{2}:[0-9]{2})?"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)
                // Partial DateTime with time: @YYYY-MM-DDTHH, @YYYY-MM-DDTHH:MM (hour/minute without seconds)
                .Match(Span.Regex(@"@[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}(:[0-9]{2})?"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)
                // Partial DateTime date-only: @YYYY-MM-DDT, @YYYY-MMT, @YYYYT (trailing T indicates DateTime precision)
                .Match(Span.Regex(@"@[0-9]{4}(-[0-9]{2}(-[0-9]{2})?)?T"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)

                // Time literals: @THH, @THH:MM, @THH:MM:SS, @THH:MM:SS.FFF (no timezone per spec)
                // Partial times allowed - hour is required, minutes/seconds optional
                .Match(Span.Regex(@"@T[0-9]{2}(:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?)?"),
                       FhirPathTokenKind.TimeLiteral, requireDelimiters: false)

                // Date literals: @YYYY, @YYYY-MM, @YYYY-MM-DD (partial precision, no time component)
                .Match(Span.Regex(@"@[0-9]{4}(-[0-9]{2}(-[0-9]{2})?)?"),
                       FhirPathTokenKind.DateLiteral, requireDelimiters: false)

                // String literals (single-quoted, SQL-style '' or backslash escapes)
                .Match(Span.Regex(@"'([^'\\]|''|\\['""\\rnft/`]|\\u[0-9a-fA-F]{4})*'"),
                       FhirPathTokenKind.StringLiteral)

                // Delimited identifiers (backtick or legacy double-quote style)
                .Match(Span.Regex("`[^`]*`"),
                       FhirPathTokenKind.DelimitedIdentifier, requireDelimiters: false) // backtick
                .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""),
                       FhirPathTokenKind.DelimitedIdentifier, requireDelimiters: false) // double-quote (legacy)

                // External constants: %identifier or %`delimited-identifier`
                .Match(Span.Regex(@"%(`[^`]*`|[a-zA-Z_][a-zA-Z0-9_]*)"),
                       FhirPathTokenKind.ExternalConstant, requireDelimiters: false)

                // Axis references: $this, $index, $total
                .Match(Span.Regex(@"\$(this|index|total)\b"),
                       FhirPathTokenKind.Axis, requireDelimiters: false)

                // Numeric literals (decimal must have '.' to distinguish from integer)
                // Long literals must be matched before integer to capture the 'L' suffix
                .Match(Span.Regex(@"[0-9]+[Ll]"), FhirPathTokenKind.LongLiteral, requireDelimiters: true)
                .Match(Span.Regex(@"[0-9]+\.[0-9]+"), FhirPathTokenKind.DecimalLiteral, requireDelimiters: true)
                .Match(Span.Regex(@"[0-9]+"), FhirPathTokenKind.IntegerLiteral, requireDelimiters: true)

                // Identifiers (letters/underscore start, alphanumeric/underscore continuation)
                .Match(Identifier.CStyle, FhirPathTokenKind.Identifier)

                // Multi-character operators (longest first to avoid partial matches)
                .Match(Span.EqualTo("<="), FhirPathTokenKind.LessThanOrEqual)
                .Match(Span.EqualTo(">="), FhirPathTokenKind.GreaterThanOrEqual)
                .Match(Span.EqualTo("!="), FhirPathTokenKind.NotEquals)
                .Match(Span.EqualTo("!~"), FhirPathTokenKind.NotEquivalent)

                // Single-character operators
                .Match(Character.EqualTo('+'), FhirPathTokenKind.Plus)
                .Match(Character.EqualTo('-'), FhirPathTokenKind.Minus)
                .Match(Character.EqualTo('*'), FhirPathTokenKind.Multiply)
                .Match(Character.EqualTo('/'), FhirPathTokenKind.Divide)
                .Match(Character.EqualTo('&'), FhirPathTokenKind.Ampersand)
                .Match(Character.EqualTo('|'), FhirPathTokenKind.Union)
                .Match(Character.EqualTo('='), FhirPathTokenKind.Equals)
                .Match(Character.EqualTo('~'), FhirPathTokenKind.Equivalent)
                .Match(Character.EqualTo('<'), FhirPathTokenKind.LessThan)
                .Match(Character.EqualTo('>'), FhirPathTokenKind.GreaterThan)
                .Match(Character.EqualTo('('), FhirPathTokenKind.LeftParen)
                .Match(Character.EqualTo(')'), FhirPathTokenKind.RightParen)
                .Match(Character.EqualTo('['), FhirPathTokenKind.LeftBracket)
                .Match(Character.EqualTo(']'), FhirPathTokenKind.RightBracket)
                .Match(Character.EqualTo('{'), FhirPathTokenKind.LeftBrace)
                .Match(Character.EqualTo('}'), FhirPathTokenKind.RightBrace)
                .Match(Character.EqualTo(','), FhirPathTokenKind.Comma)
                .Match(Character.EqualTo('.'), FhirPathTokenKind.Dot)

                .Build();
        }

        /// <summary>
        /// Creates a tokenizer that ignores whitespace and comments for faster evaluation.
        /// Use this for standard expression execution where trivia is not needed.
        /// </summary>
        public static Tokenizer<FhirPathTokenKind> Create()
        {
            // Build tokenizer without trivia tokens for performance
            return new TokenizerBuilder<FhirPathTokenKind>()
                // Comments (must come before operators to avoid capturing // as division + division)
                .Ignore(Comment.CStyle)
                .Ignore(Comment.CPlusPlusStyle)

                // Keywords (must come before identifiers, case-sensitive per FHIRPath N1.0 spec, require word boundary)
                // Use regex with \b word boundaries to prevent matching within identifiers (e.g., "is" in "issued")
                .Match(Span.Regex(@"\band\b"), FhirPathTokenKind.And, requireDelimiters: false)
                .Match(Span.Regex(@"\bor\b"), FhirPathTokenKind.Or, requireDelimiters: false)
                .Match(Span.Regex(@"\bxor\b"), FhirPathTokenKind.Xor, requireDelimiters: false)
                .Match(Span.Regex(@"\bimplies\b"), FhirPathTokenKind.Implies, requireDelimiters: false)
                .Match(Span.Regex(@"\bis\b"), FhirPathTokenKind.Is, requireDelimiters: false)
                .Match(Span.Regex(@"\bas\b"), FhirPathTokenKind.As, requireDelimiters: false)
                .Match(Span.Regex(@"\bdiv\b"), FhirPathTokenKind.Div, requireDelimiters: false)
                .Match(Span.Regex(@"\bmod\b"), FhirPathTokenKind.Mod, requireDelimiters: false)
                .Match(Span.Regex(@"\bin\b"), FhirPathTokenKind.In, requireDelimiters: false)
                .Match(Span.Regex(@"\bcontains\b"), FhirPathTokenKind.Contains, requireDelimiters: false)

                // Boolean literals (keywords, case-sensitive per spec)
                .Match(Span.Regex(@"\btrue\b"), FhirPathTokenKind.BooleanLiteral, requireDelimiters: false)
                .Match(Span.Regex(@"\bfalse\b"), FhirPathTokenKind.BooleanLiteral, requireDelimiters: false)

                // DateTime literals (must have 'T' to distinguish from Date)
                // Full DateTime: @YYYY-MM-DDTHH:MM:SS.FFF(Z|±HH:MM)?
                .Match(Span.Regex(@"@[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?(Z|[+-][0-9]{2}:[0-9]{2})?"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)
                // Partial DateTime with time: @YYYY-MM-DDTHH, @YYYY-MM-DDTHH:MM (hour/minute without seconds)
                .Match(Span.Regex(@"@[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}(:[0-9]{2})?"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)
                // Partial DateTime date-only: @YYYY-MM-DDT, @YYYY-MMT, @YYYYT (trailing T indicates DateTime precision)
                .Match(Span.Regex(@"@[0-9]{4}(-[0-9]{2}(-[0-9]{2})?)?T"),
                       FhirPathTokenKind.DateTimeLiteral, requireDelimiters: false)

                // Time literals: @THH, @THH:MM, @THH:MM:SS, @THH:MM:SS.FFF (no timezone per spec)
                // Partial times allowed - hour is required, minutes/seconds optional
                .Match(Span.Regex(@"@T[0-9]{2}(:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?)?"),
                       FhirPathTokenKind.TimeLiteral, requireDelimiters: false)

                // Date literals: @YYYY, @YYYY-MM, @YYYY-MM-DD (partial precision, no time component)
                .Match(Span.Regex(@"@[0-9]{4}(-[0-9]{2}(-[0-9]{2})?)?"),
                       FhirPathTokenKind.DateLiteral, requireDelimiters: false)

                // String literals (single-quoted, SQL-style '' or backslash escapes)
                .Match(Span.Regex(@"'([^'\\]|''|\\['""\\rnft/`]|\\u[0-9a-fA-F]{4})*'"),
                       FhirPathTokenKind.StringLiteral)

                // Delimited identifiers (backtick or legacy double-quote style)
                .Match(Span.Regex("`[^`]*`"),
                       FhirPathTokenKind.DelimitedIdentifier, requireDelimiters: false) // backtick
                .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""),
                       FhirPathTokenKind.DelimitedIdentifier, requireDelimiters: false) // double-quote (legacy)

                // External constants: %identifier or %`delimited-identifier`
                .Match(Span.Regex(@"%(`[^`]*`|[a-zA-Z_][a-zA-Z0-9_]*)"),
                       FhirPathTokenKind.ExternalConstant, requireDelimiters: false)

                // Axis references: $this, $index, $total
                .Match(Span.Regex(@"\$(this|index|total)\b"),
                       FhirPathTokenKind.Axis, requireDelimiters: false)

                // Numeric literals (decimal must have '.' to distinguish from integer)
                // Long literals must be matched before integer to capture the 'L' suffix
                .Match(Span.Regex(@"[0-9]+[Ll]"), FhirPathTokenKind.LongLiteral, requireDelimiters: true)
                .Match(Span.Regex(@"[0-9]+\.[0-9]+"), FhirPathTokenKind.DecimalLiteral, requireDelimiters: true)
                .Match(Span.Regex(@"[0-9]+"), FhirPathTokenKind.IntegerLiteral, requireDelimiters: true)

                // Identifiers (letters/underscore start, alphanumeric/underscore continuation)
                .Match(Identifier.CStyle, FhirPathTokenKind.Identifier)

                // Multi-character operators (longest first to avoid partial matches)
                .Match(Span.EqualTo("<="), FhirPathTokenKind.LessThanOrEqual)
                .Match(Span.EqualTo(">="), FhirPathTokenKind.GreaterThanOrEqual)
                .Match(Span.EqualTo("!="), FhirPathTokenKind.NotEquals)
                .Match(Span.EqualTo("!~"), FhirPathTokenKind.NotEquivalent)

                // Single-character operators
                .Match(Character.EqualTo('+'), FhirPathTokenKind.Plus)
                .Match(Character.EqualTo('-'), FhirPathTokenKind.Minus)
                .Match(Character.EqualTo('*'), FhirPathTokenKind.Multiply)
                .Match(Character.EqualTo('/'), FhirPathTokenKind.Divide)
                .Match(Character.EqualTo('&'), FhirPathTokenKind.Ampersand)
                .Match(Character.EqualTo('|'), FhirPathTokenKind.Union)
                .Match(Character.EqualTo('='), FhirPathTokenKind.Equals)
                .Match(Character.EqualTo('~'), FhirPathTokenKind.Equivalent)
                .Match(Character.EqualTo('<'), FhirPathTokenKind.LessThan)
                .Match(Character.EqualTo('>'), FhirPathTokenKind.GreaterThan)
                .Match(Character.EqualTo('('), FhirPathTokenKind.LeftParen)
                .Match(Character.EqualTo(')'), FhirPathTokenKind.RightParen)
                .Match(Character.EqualTo('['), FhirPathTokenKind.LeftBracket)
                .Match(Character.EqualTo(']'), FhirPathTokenKind.RightBracket)
                .Match(Character.EqualTo('{'), FhirPathTokenKind.LeftBrace)
                .Match(Character.EqualTo('}'), FhirPathTokenKind.RightBrace)
                .Match(Character.EqualTo(','), FhirPathTokenKind.Comma)
                .Match(Character.EqualTo('.'), FhirPathTokenKind.Dot)

                // Whitespace (ignore for standard parsing)
                .Ignore(Span.WhiteSpace)

                .Build();
        }
    }
