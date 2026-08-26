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
/// constant" - and PR #442's final review found the three had drifted: two of them accepted an empty
/// suffix (<c>%`vs-`</c>, <c>%`ext-`</c>) that the third rejects, so an expression could pass analysis
/// or SQL-on-FHIR validation clean and then throw "undefined environment variable" at evaluation - the
/// exact failure shape this branch exists to remove. Each site now asks this class instead of repeating
/// the prefix-and-length test, so there is one place left to get it right.
/// </remarks>
internal static class StandardConstantFamilies
{
    private const string ValueSetPrefix = "vs-";
    private const string ExtensionPrefix = "ext-";

    /// <summary>
    /// Whether <paramref name="name"/> is a reference into the <c>vs-</c> or <c>ext-</c> family: the
    /// backtick-delimited spelling (<paramref name="isDelimited"/>), the matching prefix, and at least
    /// one character of suffix after it. A bare prefix with nothing after it - <c>%`vs-`</c> - is not a
    /// reference to any ValueSet and does not match.
    /// </summary>
    public static bool IsPrefixedConstant(string name, bool isDelimited)
        => isDelimited && (IsValueSetReference(name) || IsExtensionReference(name));

    /// <summary>
    /// Whether <paramref name="name"/>, already known to be delimited, is <c>vs-</c> plus a non-empty
    /// suffix.
    /// </summary>
    public static bool IsValueSetReference(string name)
        => name.StartsWith(ValueSetPrefix, StringComparison.Ordinal) && name.Length > ValueSetPrefix.Length;

    /// <summary>
    /// Whether <paramref name="name"/>, already known to be delimited, is <c>ext-</c> plus a non-empty
    /// suffix.
    /// </summary>
    public static bool IsExtensionReference(string name)
        => name.StartsWith(ExtensionPrefix, StringComparison.Ordinal) && name.Length > ExtensionPrefix.Length;

    /// <summary>The part of <paramref name="name"/> after <c>vs-</c>. Only valid once <see cref="IsValueSetReference"/> is true.</summary>
    public static string ValueSetSuffix(string name) => name[ValueSetPrefix.Length..];

    /// <summary>The part of <paramref name="name"/> after <c>ext-</c>. Only valid once <see cref="IsExtensionReference"/> is true.</summary>
    public static string ExtensionSuffix(string name) => name[ExtensionPrefix.Length..];
}
