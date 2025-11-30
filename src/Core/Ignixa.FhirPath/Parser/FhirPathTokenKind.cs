/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath token definitions for Superpower parser.
 * Based on FhirPath N1.0 (Normative) specification.
 */

namespace Ignixa.FhirPath.Parser;

/// <summary>
/// Token kinds for FhirPath expressions.
/// Defines all lexical elements recognized by the FhirPath tokenizer.
/// </summary>
public enum FhirPathTokenKind
    {
        // Literals
        /// <summary>'hello world' - Single-quoted string with escape sequences</summary>
        StringLiteral,

        /// <summary>42 - Integer number</summary>
        IntegerLiteral,

        /// <summary>3.14 - Decimal number</summary>
        DecimalLiteral,

        /// <summary>true, false - Boolean constant</summary>
        BooleanLiteral,

        /// <summary>@2024-01-01 - Date literal (partial precision supported)</summary>
        DateLiteral,

        /// <summary>@2024-01-01T12:00:00Z - DateTime literal (partial precision supported)</summary>
        DateTimeLiteral,

        /// <summary>@T12:00:00 - Time literal (no timezone)</summary>
        TimeLiteral,

        // Identifiers
        /// <summary>name, Patient - Unquoted identifier</summary>
        Identifier,

        /// <summary>`quoted id`, "legacy" - Delimited identifier (backtick or double-quote)</summary>
        DelimitedIdentifier,

        /// <summary>%context, %ext-id - External constant reference</summary>
        ExternalConstant,

        /// <summary>$this, $index, $total - Axis reference</summary>
        Axis,

        // Operators (Arithmetic)
        /// <summary>+ - Addition or unary plus</summary>
        Plus,

        /// <summary>- - Subtraction or unary minus</summary>
        Minus,

        /// <summary>* - Multiplication</summary>
        Multiply,

        /// <summary>/ - Division</summary>
        Divide,

        /// <summary>div - Integer division (keyword)</summary>
        Div,

        /// <summary>mod - Modulo (keyword)</summary>
        Mod,

        /// <summary>&amp; - String concatenation</summary>
        Ampersand,

        // Operators (Collection)
        /// <summary>| - Union (distinct)</summary>
        Union,

        // Operators (Comparison)
        /// <summary>= - Equality (with null propagation)</summary>
        Equals,

        /// <summary>!= - Not equal (with null propagation)</summary>
        NotEquals,

        /// <summary>~ - Equivalence (no null propagation)</summary>
        Equivalent,

        /// <summary>!~ - Not equivalent (no null propagation)</summary>
        NotEquivalent,

        /// <summary>&lt; - Less than</summary>
        LessThan,

        /// <summary>&lt;= - Less than or equal</summary>
        LessThanOrEqual,

        /// <summary>&gt; - Greater than</summary>
        GreaterThan,

        /// <summary>&gt;= - Greater than or equal</summary>
        GreaterThanOrEqual,

        // Keywords (Type operators)
        /// <summary>is - Type checking (keyword)</summary>
        Is,

        /// <summary>as - Type casting (keyword)</summary>
        As,

        // Keywords (Logic)
        /// <summary>and - Logical AND (short-circuit, keyword)</summary>
        And,

        /// <summary>or - Logical OR (short-circuit, keyword)</summary>
        Or,

        /// <summary>xor - Logical XOR (keyword)</summary>
        Xor,

        /// <summary>implies - Logical implication (keyword)</summary>
        Implies,

        /// <summary>in - Membership test (keyword)</summary>
        In,

        /// <summary>contains - Membership test (keyword)</summary>
        Contains,

        // Delimiters
        /// <summary>( - Left parenthesis</summary>
        LeftParen,

        /// <summary>) - Right parenthesis</summary>
        RightParen,

        /// <summary>[ - Left square bracket</summary>
        LeftBracket,

        /// <summary>] - Right square bracket</summary>
        RightBracket,

        /// <summary>{ - Left curly brace</summary>
        LeftBrace,

        /// <summary>} - Right curly brace</summary>
        RightBrace,

        /// <summary>, - Comma</summary>
        Comma,

        /// <summary>. - Dot (member access)</summary>
        Dot,

        // Trivia (captured for round-tripping)
        /// <summary>Whitespace (spaces, tabs, newlines)</summary>
        Whitespace,

        /// <summary>// comment - Single-line comment</summary>
        LineComment,

        /// <summary>/* comment */ - Multi-line comment</summary>
        BlockComment,
    }
