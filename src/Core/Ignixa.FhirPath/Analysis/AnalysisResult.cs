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
    /// Gets whether the analysis found no errors.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationIssueSeverity.Error);

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
