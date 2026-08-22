/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Parse tree node for date, dateTime and time literals.
 */

namespace Ignixa.FhirPath.Parsing.ParseTree;

/// <summary>
/// A constant produced by a date, dateTime or time token rather than by a string token.
/// </summary>
/// <remarks>
/// A temporal literal and a string literal that happens to begin with <c>@</c> carry byte-identical values,
/// so the tokenizer's verdict has to be carried structurally. Every consumer that treats a constant as a
/// string still sees one; only consumers that ask the node's kind learn the difference.
/// </remarks>
internal sealed record TemporalConstantParseNode : ConstantParseNode
{
    public TemporalConstantParseNode(string literal, SourceLocation location)
        : base(literal, location)
    {
        Literal = literal;
    }

    /// <summary>
    /// Gets the literal's source text, sigil included, as the tokenizer matched it.
    /// </summary>
    public string Literal { get; }
}
