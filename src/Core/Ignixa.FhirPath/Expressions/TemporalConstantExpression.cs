/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A temporal literal, carried as its own AST node so the parser's token choice survives into analysis.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// A date, dateTime or time literal, distinguished from a string literal by the token the parser matched.
/// </summary>
/// <remarks>
/// <para>
/// The <c>@</c> sigil is not evidence of anything by the time a literal reaches the AST. The grammar keeps
/// the sigil in a temporal literal's value and strips only the surrounding quotes from a string literal, so
/// the FHIRPath expressions <c>@x</c> and <c>'@x'</c> both arrive as the CLR string <c>"@x"</c>. No
/// predicate over that string can separate them, which is why the distinction is recorded here, at the only
/// point where it is still known, rather than recovered later by inspecting the value.
/// </para>
/// <para>
/// Which temporal kind the literal denotes stays derived from the literal text. That derivation is
/// unambiguous once the node is known to be temporal, and keeping it here rather than reading it off the
/// token kind means a genuine temporal literal reaches exactly the element the evaluator built before.
/// </para>
/// </remarks>
public sealed class TemporalConstantExpression : ConstantExpression
{
    public TemporalConstantExpression(string literal, ISourcePositionInfo? location = null)
        : base(literal, location)
    {
        var body = literal.StartsWith('@') ? literal[1..] : literal;

        if (body.StartsWith('T'))
        {
            TemporalTypeName = "time";
            ElementValue = body[1..];
        }
        else if (body.Contains('T', StringComparison.Ordinal))
        {
            TemporalTypeName = "dateTime";
            ElementValue = body;
        }
        else
        {
            TemporalTypeName = "date";
            ElementValue = body;
        }
    }

    /// <summary>
    /// Gets the FHIRPath type this literal constructs: <c>date</c>, <c>dateTime</c> or <c>time</c>.
    /// </summary>
    public string TemporalTypeName { get; }

    /// <summary>
    /// Gets the literal text without the <c>@</c> sigil and, for a time, without the <c>T</c> marker.
    /// </summary>
    public string ElementValue { get; }
}
