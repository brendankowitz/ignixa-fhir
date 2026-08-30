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
/// <para>
/// A temporal literal and a string literal that happens to begin with <c>@</c> carry byte-identical
/// values, so the tokenizer's verdict has to be carried structurally. Every consumer that treats a
/// constant as a string still sees one; only consumers that ask the node's kind learn the difference.
/// </para>
/// <para>
/// The kind is the entire payload; the literal's source text is <see cref="ConstantParseNode.Value"/>
/// and nothing else. It used to be mirrored into a <c>Literal</c> property declared by hand on a
/// positional record, which put it outside the generated equality, <c>GetHashCode</c> and
/// <c>PrintMembers</c>, and left a <c>with</c> expression able to desync the two copies of one literal.
/// </para>
/// </remarks>
internal sealed record TemporalConstantParseNode : ConstantParseNode
{
    public TemporalConstantParseNode(string literal, SourceLocation location)
        : base(literal, location)
    {
    }
}
