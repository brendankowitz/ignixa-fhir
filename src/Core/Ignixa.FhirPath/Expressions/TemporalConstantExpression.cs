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
/// <para>
/// This node reports the kind the grammar assigned; it is deliberately not the authority on whether the
/// literal is a <em>valid</em> value of that kind. The tokenizer's <c>DateTimeLiteral</c> production
/// admits a trailing bare <c>T</c>, so <c>@2013T</c> and <c>@2013-06-15T</c> reach here and become
/// <c>dateTime</c> elements whose values no FHIR <c>dateTime</c> would accept. Rejecting them is a
/// lexical judgement, and making a semantic node throw on input the grammar admits is a different change
/// with a different blast radius; the fix belongs in the grammar if it is wanted. Measured, the engine
/// already degrades visibly rather than silently: <c>@2013T.convertsToDateTime()</c> is false,
/// <c>@2013T.lowBoundary()</c> and <c>@2013T &lt; @2014</c> are both empty, and equality falls back to
/// comparing the text. Nothing claims a wrong instant.
/// </para>
/// </remarks>
public sealed class TemporalConstantExpression : ConstantExpression
{
    public TemporalConstantExpression(string literal, ISourcePositionInfo? location = null)
        : base(ThrowIfNull(literal), location)
    {
        Literal = literal;

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
    /// Gets the literal's source text, sigil included, as it was written.
    /// </summary>
    public string Literal { get; }

    /// <summary>
    /// Gets the FHIRPath type this literal constructs: <c>date</c>, <c>dateTime</c> or <c>time</c>.
    /// </summary>
    public string TemporalTypeName { get; }

    /// <summary>
    /// Gets the literal text without the <c>@</c> sigil and, for a time, without the <c>T</c> marker.
    /// </summary>
    public string ElementValue { get; }

    /// <remarks>
    /// The base constructor takes the literal as its <c>value</c> parameter, so letting a null reach it
    /// reported <c>paramName: "value"</c> for an argument this constructor calls <c>literal</c>.
    /// </remarks>
    private static string ThrowIfNull(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        return literal;
    }
}
