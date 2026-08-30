/*
 * Copyright (c) 2025, Ignixa Contributors
 */

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Which family of FHIRPath type operator is asking <see cref="TypeMatcher.IsTypeMatch"/> a question.
/// </summary>
/// <remarks>
/// <para>
/// The two families agree on the part that matters and that FHIRPath actually specifies - the subclass
/// walk over complex types and the resource hierarchy - and differ on exactly two axes, each of which
/// FHIR (not FHIRPath) states explicitly. Nothing else about them may diverge; see
/// <see cref="TypeMatcher"/>.
/// </para>
/// </remarks>
internal enum TypeMatchMode
{
    /// <summary>
    /// <c>is</c> and <c>is()</c>. Primitive specialization edges are followed, so a <c>code</c> is a
    /// <c>string</c>; and the System/FHIR namespace distinction is enforced, so an unqualified
    /// capitalized <c>String</c> means <c>System.String</c> and does not match a FHIR <c>string</c>.
    /// </summary>
    TypeTest,

    /// <summary>
    /// <c>as</c>, <c>as()</c> and <c>ofType()</c>. Primitive specialization edges are cut, so a
    /// <c>code</c> is not a <c>string</c>; and the namespace qualifier is stripped rather than enforced,
    /// so <c>as(DateTime)</c> matches a FHIR <c>dateTime</c>.
    /// </summary>
    Cast
}
