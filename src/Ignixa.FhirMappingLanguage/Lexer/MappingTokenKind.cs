/*
 * Copyright (c) 2025, Ignixa Contributors
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
#pragma warning disable CA1720 // Identifier contains type name - 'Single' is a FHIR spec keyword for list modes
    Single,
#pragma warning restore CA1720

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
    Colon,               // :
    Range,               // .. (for cardinality)
    Dot,                 // .
    Comma,               // ,
    Semicolon,           // ;
    Asterisk,            // * (for unbounded cardinality)

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
