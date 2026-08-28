/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Recognition rule for the FHIRPath %`vs-name` / %`ext-name` constant families, shared by the
 * evaluator, the static analyzer, and the SQL-on-FHIR ViewDefinition validator.
 */

namespace Ignixa.FhirPath;

/// <summary>
/// The single rule for what counts as a reference into the <c>vs-</c> / <c>ext-</c> external constant
/// families the FHIR profile of FHIRPath defines.
/// </summary>
/// <remarks>
/// Three call sites need this exact question answered - <see cref="Evaluation.EvaluationContext"/> to
/// resolve the value, <see cref="Analysis.AnalysisContext"/> to type the reference, and
/// <c>Ignixa.SqlOnFhir.Parsing.ViewDefinitionExpressionParser</c> to exempt it from "must be a defined
/// constant" - and independently re-deriving it let them drift: two accepted an empty suffix
/// (<c>%`vs-`</c>, <c>%`ext-`</c>) that the third rejected, so an expression could pass analysis or
/// SQL-on-FHIR validation clean and then throw "undefined environment variable" at evaluation. Each site
/// asks this class instead of repeating the prefix-and-length test.
/// </remarks>
internal static class StandardConstantFamilies
{
    private const string ValueSetPrefix = "vs-";
    private const string ExtensionPrefix = "ext-";
    private const string ValueSetUrlBase = "http://hl7.org/fhir/ValueSet/";
    private const string ExtensionUrlBase = "http://hl7.org/fhir/StructureDefinition/";

    /// <summary>
    /// Whether <paramref name="name"/> is a reference into the <c>vs-</c> or <c>ext-</c> family: delimited
    /// spelling, matching prefix, and at least one character of suffix. A bare prefix - <c>%`vs-`</c> - is
    /// not a reference to any ValueSet and does not match. Defined as "resolves to a URL" rather than
    /// re-derived, so the recogniser and the expansion cannot disagree.
    /// </summary>
    public static bool IsPrefixedConstant(string name, bool isDelimited)
        => TryResolveCanonicalUrl(name, isDelimited, out _);

    /// <summary>
    /// The canonical URL <paramref name="name"/> stands for, when it is a reference into either family.
    /// </summary>
    /// <param name="name">The variable name, without the leading <c>%</c> or any surrounding backticks.</param>
    /// <param name="isDelimited">Whether the reference was written as <c>%`name`</c> rather than bare.</param>
    /// <param name="url">The ValueSet or StructureDefinition URL, or <see langword="null"/> when this is not a family reference.</param>
    /// <returns><see langword="true"/> when <paramref name="url"/> was produced.</returns>
    /// <remarks>
    /// The two URL bases live here rather than at the evaluator's call site because they are the other
    /// half of the same rule: the FHIR profile of FHIRPath defines <c>%`vs-[name]`</c> and
    /// <c>%`ext-[name]`</c> as shorthands <em>for these URLs</em>. Splitting the naming test from the
    /// expansion would let a name be recognised as a family member and then expand to nothing.
    /// </remarks>
    public static bool TryResolveCanonicalUrl(string name, bool isDelimited, out string? url)
    {
        url = null;

        if (!isDelimited)
        {
            return false;
        }

        if (HasNonEmptySuffix(name, ValueSetPrefix))
        {
            url = ValueSetUrlBase + name[ValueSetPrefix.Length..];
        }
        else if (HasNonEmptySuffix(name, ExtensionPrefix))
        {
            url = ExtensionUrlBase + name[ExtensionPrefix.Length..];
        }

        return url is not null;
    }

    private static bool HasNonEmptySuffix(string name, string prefix)
        => name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length;
}
