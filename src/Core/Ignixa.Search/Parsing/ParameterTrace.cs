// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Parsing;

/// <summary>
/// One parameter's trace: its position, source text, projected syntax, IR, and outcome.
/// </summary>
/// <remarks>
/// <see cref="KeySyntax"/> and <see cref="ValueSyntax"/> mirror <see cref="ParseResult"/>'s two syntax
/// projections. Structural provenance — chain segments, modifiers, include shape — lives in
/// <see cref="KeySyntax"/>; value structure — alternatives, composites, atomics — lives in
/// <see cref="ValueSyntax"/>. Both are nullable: <see cref="ValueSyntax"/> is legitimately null for
/// shapes with no value tree (<c>_not-referenced</c>, includes), and either may be null when a
/// parameter is <see cref="ParameterOutcome.Ignored"/> or <see cref="ParameterOutcome.Failed"/> before
/// parsing completes.
/// <para>
/// <see cref="Ir"/> is a live <see cref="Expression"/> graph, not a data transfer object: it holds resolved
/// <see cref="Models.SearchParameterInfo"/> and <see cref="Indexing.SearchValues.ISearchValue"/> instances
/// and is not serializable. Anything crossing a wire or reaching a renderer must go through
/// <see cref="IrProjector.Describe"/>, which flattens it to <see cref="IrRow"/>s.
/// </para>
/// <para>
/// Construction validates <see cref="Ordinal"/>, <see cref="Key"/>, <see cref="Value"/> and
/// <see cref="Outcome"/>. <see cref="Ordinal"/> earns its guard by being what
/// <see cref="Sql.CteProvenance.ParameterOrdinal"/> is built from — an unchecked value here
/// surfaces as a failure several stages downstream, naming a parameter the producer never touched.
/// <see cref="Outcome"/> is the only <c>init</c> property, because later stages legitimately restamp it
/// when lowering or emission fails; every other property is get-only so a <c>with</c> cannot copy around
/// a check. (<see cref="Outcome"/>'s own null guard is therefore bypassable by
/// <c>with { Outcome = null! }</c> — accepted, since that requires suppressing nullability to reach.)
/// </para>
/// <para>
/// The parameter order groups <see cref="Key"/> with <see cref="KeySyntax"/> and <see cref="Value"/> with
/// <see cref="ValueSyntax"/>. That reads better than separating each pair, but it is not a safety
/// mechanism: <see cref="Key"/> and <see cref="Value"/> are both <see cref="string"/>, so transposing them
/// still compiles, and the test fixtures construct these positionally. The production call sites in
/// <see cref="SearchOptionsBuilder"/> use named arguments by convention; nothing enforces it.
/// </para>
/// </remarks>
public sealed record ParameterTrace
{
    public ParameterTrace(
        int ordinal,
        string key,
        SyntaxNode? keySyntax,
        string value,
        SyntaxNode? valueSyntax,
        Expression? ir,
        ParameterOutcome outcome,
        SearchParamType? dataType)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(outcome);

        Ordinal = ordinal;
        Key = key;
        KeySyntax = keySyntax;
        Value = value;
        ValueSyntax = valueSyntax;
        Ir = ir;
        Outcome = outcome;
        DataType = dataType;
    }

    /// <summary>Position among the search parameters of this request, dense from zero.</summary>
    public int Ordinal { get; }

    /// <summary>The parameter name as written, including any modifier.</summary>
    public string Key { get; }

    public SyntaxNode? KeySyntax { get; }

    /// <summary>The parameter value as written. Empty for shapes that carry no value.</summary>
    public string Value { get; }

    public SyntaxNode? ValueSyntax { get; }

    public Expression? Ir { get; }

    /// <summary>
    /// How this parameter fared. The only settable property: <c>SearchSqlCompiler</c>
    /// restamps it with a <see cref="ParameterOutcome.Failed"/> when a later stage attributes a failure
    /// back to this parameter.
    /// </summary>
    public ParameterOutcome Outcome { get; init; }

    /// <summary>
    /// The declared type of the search parameter this value was matched against — see
    /// <see cref="ParseResult.DataType"/>. Null when the parameter never bound a value, which includes
    /// every parameter that was ignored before parsing completed.
    /// </summary>
    public SearchParamType? DataType { get; }
}
