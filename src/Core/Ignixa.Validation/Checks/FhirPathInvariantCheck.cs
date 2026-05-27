// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;
using ConstraintDefinition = Ignixa.Specification.ConstraintDefinition;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIRPath invariant constraints.
/// Used in Spec and Full validation depths.
/// </summary>
/// <remarks>
/// This check evaluates FHIRPath constraint expressions defined in FHIR StructureDefinitions.
/// Examples: ele-1 (all elements must have @value or children), dom-1 (contained resources must have id).
/// Uses lazy compilation for performance - expressions are parsed once and cached.
/// </remarks>
public class FhirPathInvariantCheck : IValidationCheck
{
    private readonly IConstraint _constraint;
    private readonly ISchema _schema;
    private readonly FhirPathParser _parser;
    private readonly IReadOnlyList<string> _appliesTo;
    private readonly ILogger? _logger;
    private readonly Lazy<FhirPathEvaluator> _evaluator;
    private readonly Lazy<FhirPath.Expressions.Expression> _compiledExpression;

    /// <summary>
    /// Gets the constraint key (e.g., "ele-1", "ext-1", "bdl-5").
    /// </summary>
    public string ConstraintKey => _constraint.Key;

    /// <summary>
    /// Gets the resource/datatype names this constraint applies to.
    /// Empty collection means "applies to all" (the default for constraints sourced
    /// from <see cref="IConstraint"/> implementations that don't expose scope metadata).
    /// </summary>
    public IReadOnlyList<string> AppliesTo => _appliesTo;

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathInvariantCheck"/> class
    /// from any <see cref="IConstraint"/>. This is the canonical ctor used by
    /// <c>StructureDefinitionSchemaBuilder</c>.
    /// </summary>
    /// <param name="constraint">The constraint to evaluate.</param>
    /// <param name="schema">Schema provider for FHIRPath type information.</param>
    /// <param name="parser">Shared FhirPath parser instance.</param>
    /// <param name="appliesTo">
    /// Resource/datatype names this constraint applies to. Empty/null = applies to all.
    /// When non-empty, <see cref="Validate"/> short-circuits to Success for elements
    /// whose <c>InstanceType</c> is not in the list.
    /// </param>
    public FhirPathInvariantCheck(
        IConstraint constraint,
        ISchema schema,
        FhirPathParser parser,
        IReadOnlyList<string>? appliesTo = null,
        ILogger? logger = null)
    {
        _constraint = constraint ?? throw new ArgumentNullException(nameof(constraint));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _appliesTo = appliesTo ?? Array.Empty<string>();
        _logger = logger;

        // Lazy compilation - parse FHIRPath expression only when first needed
        _evaluator = new Lazy<FhirPathEvaluator>(() => new FhirPathEvaluator());
        _compiledExpression = new Lazy<FhirPath.Expressions.Expression>(() =>
        {
            try
            {
                return _parser.Parse(_constraint.Expression);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to parse FHIRPath expression for constraint {ConstraintKey} - constraint will be skipped", _constraint.Key);
                return new FhirPath.Expressions.EmptyExpression();
            }
        });
    }

    /// <summary>
    /// Backwards-compatible ctor preserving the historical
    /// <see cref="ConstraintDefinition"/> (Specification record) entry point used by
    /// existing tests and direct consumers. Delegates to the <see cref="IConstraint"/>
    /// ctor, honoring the <c>AppliesTo</c> scope carried on the record.
    /// </summary>
    /// <param name="constraint">The constraint definition to evaluate.</param>
    /// <param name="schema">Schema provider for type information.</param>
    /// <param name="parser">FhirPath compiler for parsing expressions (shared across checks).</param>
    public FhirPathInvariantCheck(
        ConstraintDefinition constraint,
        ISchema schema,
        FhirPathParser parser)
        : this(
            ConvertSpecificationConstraint(constraint),
            schema,
            parser,
            constraint?.AppliesTo)
    {
    }

    private static IConstraint ConvertSpecificationConstraint(ConstraintDefinition c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new Ignixa.Abstractions.ConstraintDefinition
        {
            Key = c.Key,
            Expression = c.Expression,
            Human = c.Human,
            Severity = c.Severity == ConstraintSeverity.Warning ? "warning" : "error",
            Xpath = c.Xpath,
        };
    }

    /// <summary>
    /// Validates a FHIR element against this constraint's FHIRPath expression.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Skip invariant validation if depth is Minimal (invariants are Spec depth and above)
        if (settings.Depth < ValidationDepth.Spec)
        {
            return ValidationResult.Success();
        }

        // Scope filter: skip when AppliesTo is set and the element's resource type is out of scope.
        if (_appliesTo.Count > 0)
        {
            var instanceType = element.InstanceType;
            if (!string.IsNullOrEmpty(instanceType) && !_appliesTo.Contains(instanceType))
            {
                return ValidationResult.Success();
            }
        }

        try
        {
            // Evaluate the FHIRPath expression
            var result = _evaluator.Value.Evaluate(element, _compiledExpression.Value);

            // Convert result to boolean
            // Per FHIRPath spec: empty result = false, single boolean true = true, all else = false
            bool isValid = IsResultTrue(result);

            if (!isValid)
            {
                // IConstraint.Severity is a string ("error" / "warning"); map to IssueSeverity.
                var isWarning = string.Equals(_constraint.Severity, "warning", StringComparison.OrdinalIgnoreCase);
                var severity = isWarning ? IssueSeverity.Warning : IssueSeverity.Error;

                // Create validation issue with constraint key and human description
                var issue = ValidationIssue.InvariantFailure(
                    _constraint.Key,
                    _constraint.Human ?? string.Empty,
                    element.Location ?? string.Empty,
                    severity);

                // Warnings don't fail validation (isValid = true), but errors do
                if (isWarning)
                {
                    return new ValidationResult(isValid: true, issues: new[] { issue });
                }

                return ValidationResult.Failure(issue);
            }

            return ValidationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var issue = new ValidationIssue(
                IssueSeverity.Error,
                _constraint.Key,
                element.Location ?? string.Empty,
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
    private static bool IsResultTrue(IEnumerable<IElement> result)
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
