// <copyright file="FhirPathInvariantCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIRPath invariant constraints.
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// This check evaluates FHIRPath constraint expressions defined in FHIR StructureDefinitions.
/// Examples: ele-1 (all elements must have @value or children), dom-1 (contained resources must have id).
/// Uses lazy compilation for performance - expressions are parsed once and cached.
/// </remarks>
public class FhirPathInvariantCheck : IValidationCheck
{
    private readonly ConstraintDefinition _constraint;
    private readonly IStructureDefinitionSummaryProvider _provider;
    private readonly FhirPathCompiler _compiler;
    private readonly Lazy<FhirPathEvaluator> _evaluator;
    private readonly Lazy<FhirPath.Expressions.Expression> _compiledExpression;

    /// <summary>
    /// Gets the constraint key (e.g., "ele-1", "ext-1", "bdl-5").
    /// </summary>
    public string ConstraintKey => _constraint.Key;

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathInvariantCheck"/> class.
    /// </summary>
    /// <param name="constraint">The constraint definition to evaluate.</param>
    /// <param name="provider">Structure definition provider for type information.</param>
    /// <param name="compiler">FhirPath compiler for parsing expressions (shared across checks).</param>
    public FhirPathInvariantCheck(
        ConstraintDefinition constraint,
        IStructureDefinitionSummaryProvider provider,
        FhirPathCompiler compiler)
    {
        _constraint = constraint ?? throw new ArgumentNullException(nameof(constraint));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));

        // Lazy compilation - parse FHIRPath expression only when first needed
        _evaluator = new Lazy<FhirPathEvaluator>(() => new FhirPathEvaluator());
        _compiledExpression = new Lazy<FhirPath.Expressions.Expression>(() =>
        {
            try
            {
                return _compiler.Parse(_constraint.Expression);
            }
            catch (Exception ex)
            {
                // If expression parsing fails, log and return empty expression
                // This allows validation to continue even if some constraints have invalid expressions
                System.Diagnostics.Debug.WriteLine($"Failed to parse FHIRPath expression for constraint {_constraint.Key}: {ex.Message}");
                return new FhirPath.Expressions.EmptyExpression();
            }
        });
    }

    /// <summary>
    /// Validates a FHIR source node against this constraint's FHIRPath expression.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        // Skip invariant validation if tier is Fast (invariants are Spec tier and above)
        if (settings.Tier < ValidationTier.Spec)
        {
            return ValidationResult.Success();
        }

        try
        {
            // Convert ISourceNode to ITypedElement for FHIRPath evaluation
            // This requires structure definition provider for type information
            var typedElement = node.ToTypedElement(_provider);

            // Evaluate the FHIRPath expression
            var result = _evaluator.Value.Evaluate(typedElement, _compiledExpression.Value);

            // Convert result to boolean
            // Per FHIRPath spec: empty result = false, single boolean true = true, all else = false
            bool isValid = IsResultTrue(result);

            if (!isValid)
            {
                // Map ConstraintSeverity to IssueSeverity
                var severity = _constraint.Severity == ConstraintSeverity.Warning
                    ? IssueSeverity.Warning
                    : IssueSeverity.Error;

                // Create validation issue with constraint key and human description
                var issue = ValidationIssue.InvariantFailure(
                    _constraint.Key,
                    _constraint.Human,
                    node.Location ?? string.Empty,
                    severity);

                // Warnings don't fail validation (isValid = true), but errors do
                if (_constraint.Severity == ConstraintSeverity.Warning)
                {
                    return new ValidationResult(isValid: true, issues: new[] { issue });
                }

                return ValidationResult.Failure(issue);
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            // If evaluation fails, treat as validation error
            // This ensures malformed data doesn't cause crashes
            var issue = new ValidationIssue(
                IssueSeverity.Error,
                _constraint.Key,
                node.Location ?? string.Empty,
                $"{_constraint.Key}: Failed to evaluate FHIRPath expression: {ex.Message}");

            return ValidationResult.Failure(issue);
        }
    }

    /// <summary>
    /// Determines if a FHIRPath evaluation result should be treated as true.
    /// Per FHIRPath spec: empty result = false, single boolean true = true, all else = false.
    /// </summary>
    /// <param name="result">The FHIRPath evaluation result.</param>
    /// <returns>True if the result represents a successful constraint evaluation.</returns>
    private static bool IsResultTrue(IEnumerable<ITypedElement> result)
    {
        var resultList = result.ToList();

        // Empty collection = false
        if (resultList.Count == 0)
        {
            return false;
        }

        // Single boolean true = true
        if (resultList.Count == 1 && resultList[0].Value is bool boolValue)
        {
            return boolValue;
        }

        // Non-empty collection (non-boolean or multiple items) = true
        // This handles cases like "children().count() > 0" which returns an integer
        // Per FHIRPath: any non-empty result is truthy
        return true;
    }
}
