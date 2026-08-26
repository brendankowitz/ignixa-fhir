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
public class VariableRefExpression : Expression
{
    /// <summary>
    /// Creates a reference to a bare <c>%name</c>.
    /// </summary>
    /// <remarks>
    /// This overload is what let <see cref="IsDelimited"/> be added without a binary break.
    /// <c>Ignixa.FhirPath</c> ships as a stable NuGet package (<c>IsPackable</c> /
    /// <c>PackageStability: stable</c>), and an optional parameter is not an overload: a caller compiled
    /// against the previous package emitted a call site naming the two-parameter constructor, so
    /// appending <c>isDelimited</c> to that constructor would have removed it from metadata and left the
    /// caller throwing <see cref="MissingMethodException"/> at run time without ever recompiling. The
    /// forwarded value is <see langword="false"/> because before <see cref="IsDelimited"/> existed no
    /// reference carried the delimited spelling, so bare is the behaviour such a caller already had.
    /// </remarks>
    public VariableRefExpression(string name, ISourcePositionInfo? location = null)
        : this(name, location, isDelimited: false)
    {
    }

    /// <summary>
    /// Creates a reference, recording whether it was written in the backtick-delimited form.
    /// </summary>
    /// <param name="name">The variable name, with the leading <c>%</c> and any backticks removed.</param>
    /// <param name="location">Where the reference appeared in the source expression.</param>
    /// <param name="isDelimited">Whether the reference was written as <c>%`name`</c>; see <see cref="IsDelimited"/>.</param>
    public VariableRefExpression(string name, ISourcePositionInfo? location, bool isDelimited)
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
