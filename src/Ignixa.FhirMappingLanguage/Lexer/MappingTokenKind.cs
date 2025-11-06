/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Token kinds for FHIR Mapping Language lexer.
 * Based on FHIR StructureMap specification.
 */

namespace Ignixa.FhirMappingLanguage.Lexer;

/// <summary>
/// Token types for FHIR Mapping Language tokenization.
/// </summary>
public enum MappingTokenKind
{
    // Keywords
    Map,
    Uses,
    As,
    Alias,
    Imports,
    Group,
    Extends,
    Default,
    Where,
    Check,
    Log,
    Then,
    Source,
    Target,
    Queried,
    Produced,
    ConceptMap,
    Prefix,
    Types,
    Type,
    First,
    NotFirst,
    Last,
    NotLast,
    OnlyOne,
    Share,
    Single,

    // Boolean literals
    True,
    False,

    // Identifiers and literals
    Identifier,
    DelimitedIdentifier,
    StringLiteral,
    IntegerLiteral,
    DecimalLiteral,
    Url,

    // Operators
    Equals,              // =
    Arrow,               // ->
    DoubleColon,         // ::
    Dot,                 // .
    Comma,               // ,
    Semicolon,           // ;

    // Delimiters
    LeftParen,           // (
    RightParen,          // )
    LeftBrace,           // {
    RightBrace,          // }
    LeftAngle,           // <
    RightAngle,          // >
    LeftBracket,         // [
    RightBracket,        // ]

    // Comments (for trivia mode)
    LineComment,         // //
    BlockComment,        // /* */

    // Whitespace (for trivia mode)
    Whitespace
}
