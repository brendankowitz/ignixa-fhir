// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Analysis;

/// <summary>
/// Represents the result of FhirPath expression analysis.
/// </summary>
/// <remarks>
/// Known limitation — type-name casing. The analyzer resolves type names case-insensitively, while
/// from R5 the evaluator matches them <c>Ordinal</c>-exact and carries an alias set only for the
/// pre-R5 versions. The two therefore disagree on a mis-cased cast:
/// <c>Observation.value.as(String)</c> analyses clean on R5 and R6 and evaluates empty, and
/// <c>as(codeableconcept)</c> or <c>as(humanname)</c> do so on every version. In those cases
/// <see cref="IsValid"/> is <see langword="true"/>, <see cref="HasAlwaysEmptySubexpression"/> is
/// <see langword="false"/>, and <see cref="InferredTypes"/> names the correctly-cased type, for an
/// expression the evaluator provably empties — so no member of this type can be used to detect that
/// class of typo. The analyzer resolves a cast target twice over, first against the focus's own types
/// and then through the schema's type lookup, and both are case-insensitive; closing either alone was
/// measured to change nothing, so aligning the two engines means version-gating both paths as the
/// evaluator already does. Until then the divergence is pinned by
/// <c>AnalyzerEvaluatorTypeCasingDivergenceTests</c>, which fails when it changes in either direction.
/// </remarks>
public sealed class AnalysisResult
{
    /// <summary>
    /// Gets or sets the inferred types for the expression.
    /// </summary>
    public FhirPathTypeSet InferredTypes { get; set; } = new();

    /// <summary>
    /// Maps each expression node to its inferred type set.
    /// Uses reference equality for Expression keys.
    /// </summary>
    public IReadOnlyDictionary<Expression, FhirPathTypeSet> NodeTypes { get; init; } 
        = new Dictionary<Expression, FhirPathTypeSet>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the validation issues found during analysis.
    /// </summary>
    public Collection<ValidationIssue> Issues { get; } = [];

    /// <summary>
    /// Gets whether the analysis found no errors and fully determined the expression's validity.
    /// </summary>
    /// <remarks>
    /// This is deliberately stricter than "no errors were reported": an expression the analyzer could not
    /// fully reason about is not certified valid. Callers writing a rejection gate should choose between
    /// this and <see cref="IsValidOrIndeterminate"/> explicitly. Against the shipped search-parameter
    /// corpus, <c>Severity == Error</c> is confined to a single defective upstream expression — 26 of
    /// 1,977 parameter/base-resource pairs on R5 and 25 of 2,027 on R6, none on STU3, R4 or R4B — and
    /// <c>IsValid</c> is additionally false wherever the result is indeterminate, up to 4.2% of pairs
    /// (83 of 1,977 on R5). A decidably always-empty navigation is neither, as a diagnostic: it is
    /// raised as a warning, so in isolation it leaves <c>IsValid</c> true and
    /// <see cref="HasAlwaysEmptySubexpression"/> is the only signal for it. That describes the
    /// diagnostic, not the corpus — there the always-empty pairs and the error pairs are the same set
    /// (26 of 26 on R5, 25 of 25 on R6), so <c>HasAlwaysEmptySubexpression &amp;&amp; IsValid</c> does
    /// not occur in the shipped vocabulary at all. Expect the combination from hand-written
    /// expressions.
    /// </remarks>
    public bool IsValid =>
        !Issues.Any(i => i.Severity == ValidationIssueSeverity.Error) &&
        !IsIndeterminate;

    /// <summary>
    /// Gets whether the analysis found no errors, treating what it could not determine as acceptable.
    /// </summary>
    /// <remarks>
    /// The permissive counterpart to <see cref="IsValid"/>, for callers that would rather admit an
    /// expression static analysis cannot decide than reject a conformant one. Inspect
    /// <see cref="IsIndeterminate"/> to tell the two admitted outcomes apart.
    /// </remarks>
    public bool IsValidOrIndeterminate =>
        !Issues.Any(i => i.Severity == ValidationIssueSeverity.Error);

    /// <summary>
    /// Gets whether the analyzer concluded that some subexpression always yields empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of <see cref="IsValid"/>. An always-empty navigation is a decided fact,
    /// so treating it as invalid would contradict the analyzer's own reasoning; but it is also the shape
    /// a typo takes once the root type is known concretely (<c>status</c> against a <c>Patient</c> root is
    /// decidably empty, and almost certainly a mistake). Neither <see cref="IsValid"/> nor
    /// <see cref="IsValidOrIndeterminate"/> separates that case from a correct expression, so this
    /// predicate exists to let a caller apply its own policy without matching on warning text. It is not
    /// evidence of a defect on its own: an expression written as a union across resource types is
    /// legitimately empty on most of them.
    /// </para>
    /// <para>
    /// This reports the analyzer's conclusion, not decidability. The classifier keys on
    /// <see cref="FhirPathTypeSet.IsRoot"/>, so only a bare root-relative name reaches the always-empty
    /// outcome: <c>Patient.status</c> provably yields empty and returns <see langword="false"/> here,
    /// as do <c>$this.status</c>, <c>%resource.status</c> and <c>Patient.where(status = 'active')</c>,
    /// all of which are reported as an unresolved-property error instead. A <see langword="false"/>
    /// result therefore means "not classified as always-empty", not "not always empty" — see also the
    /// class-level note on type-name casing, which is a second source of the same asymmetry.
    /// </para>
    /// </remarks>
    public bool HasAlwaysEmptySubexpression => Issues.Any(issue => issue.IsAlwaysEmpty);

    /// <summary>
    /// Gets whether static analysis could not determine the expression's validity.
    /// </summary>
    public bool IsIndeterminate => Issues.Any(issue => issue.IsIndeterminate);

    /// <summary>
    /// Gets whether the analysis found any warnings.
    /// </summary>
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationIssueSeverity.Warning);

    /// <summary>
    /// Gets additional metadata from the analysis.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = [];

    /// <summary>
    /// Gets the distinct type names from the inferred types.
    /// </summary>
    public IEnumerable<string> TypeNames => InferredTypes.Types.Select(t => t.TypeName).Distinct();

    /// <summary>
    /// Gets error messages from validation issues.
    /// </summary>
    public IEnumerable<string> Errors => Issues
        .Where(i => i.Severity == ValidationIssueSeverity.Error)
        .Select(i => i.Message);

    /// <summary>
    /// Gets warning messages from validation issues.
    /// </summary>
    public IEnumerable<string> Warnings => Issues
        .Where(i => i.Severity == ValidationIssueSeverity.Warning)
        .Select(i => i.Message);

    /// <summary>
    /// Creates a successful result with the specified types.
    /// </summary>
    public static AnalysisResult Success(FhirPathTypeSet types)
    {
        return new AnalysisResult { InferredTypes = types };
    }

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    public static AnalysisResult Failure(string errorMessage)
    {
        var result = new AnalysisResult();
        result.Issues.Add(new ValidationIssue
        {
            Severity = ValidationIssueSeverity.Error,
            Message = errorMessage
        });
        return result;
    }

    /// <summary>
    /// Creates a result from an analysis context.
    /// </summary>
    public static AnalysisResult FromContext(AnalysisContext context, FhirPathTypeSet types)
    {
        var result = new AnalysisResult
        {
            InferredTypes = types
        };

        foreach (var issue in context.Issues)
        {
            result.Issues.Add(issue);
        }

        return result;
    }
}
