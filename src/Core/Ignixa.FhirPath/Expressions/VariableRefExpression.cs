/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a variable reference in a FhirPath expression.
/// Examples: %context, %resource, %`ext-patient-birthTime`.
/// </summary>
/// <remarks>
/// <paramref name="isDelimited"/> on the constructor below is an optional parameter appended to an
/// existing public member of a package built with <c>IsPackable</c>/<c>PackageStability: stable</c>:
/// source-compatible for callers that recompile, but binary-breaking for one that does not, since the
/// parameter becomes part of the call site's signature.
/// </remarks>
public class VariableRefExpression : Expression
{
    public VariableRefExpression(string name, ISourcePositionInfo? location = null, bool isDelimited = false)
        : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsDelimited = isDelimited;
    }

    /// <summary>
    /// The variable name, with the leading <c>%</c> and any surrounding backticks removed.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether the reference was written in the backtick-delimited form, <c>%`name`</c>, rather than bare.
    /// </summary>
    /// <remarks>
    /// The two spellings are not interchangeable. The FHIR profile of FHIRPath writes its ValueSet and
    /// extension shorthands as <c>%`vs-[name]`</c> and <c>%`ext-[name]`</c> and says explicitly that the names
    /// "are quoted (just like paths) to allow '-' in the name"; HAPI's FHIRPathEngine expands them only for the
    /// backtick spelling, and treats a bare <c>%vs-x</c> as an ordinary - and therefore unknown - constant name.
    /// Ignixa's lexer accepts <c>-</c> in the bare form so that names like <c>%p-inactive</c>, which appear in
    /// published cqf-expression content, lex as one token the way HAPI's lexer takes them; carrying this flag is
    /// what stops that lexical allowance from silently turning into a resolution rule the spec does not have.
    /// </remarks>
    public bool IsDelimited { get; }

    public override string ToString() => $"Variable(%{Name})";

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitVariable(this, context);
}
