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
/// <para>
/// This check evaluates FHIRPath constraint expressions defined in FHIR StructureDefinitions.
/// Examples: ele-1 (all elements must have @value or children), dom-1 (contained resources must have id).
/// Uses lazy compilation for performance - expressions are parsed once and cached.
/// </para>
/// <para>
/// <b>Exception-path severity is a deliberate position, not an oversight.</b> The spec's
/// conformance-rules.html is silent on what to do when a constraint expression cannot be evaluated
/// at all. Firely's side is confirmed against source: <c>FhirPathValidator.runInvariantInternal</c>
/// catches any exception into <c>Issue.PROFILE_ELEMENTDEF_INVALID_FHIRPATH_EXPRESSION</c>, which
/// reflection over 5.11.4 gives <c>Severity = Warning, Code = 2009</c>, and <c>InvariantValidator.Validate</c>
/// returns before the declared-severity branch runs - so Firely always degrades an evaluation
/// exception to a non-failing Warning, regardless of the constraint's declared severity. Whether HAPI
/// disagrees is unconfirmed: it reportedly reports an Error and counts it toward the resource's
/// failure tally (per <c>hapifhir/org.hl7.fhir.core</c> issues #1338 and #1326 - observed from the
/// issue discussion, not confirmed against HAPI's source). This code follows Firely: a known engine
/// gap (<see cref="NotSupportedException"/>) or an expression the
/// engine correctly refuses to evaluate (<see cref="FhirPathEvaluationException"/>) never fails
/// the resource. The fhir-server ingestion seam sits downstream of this check and must not start
/// rejecting resources Firely accepts over a constraint neither engine can evaluate - HAPI's
/// stricter reading would do exactly that. An exception of any other type is treated as a genuine
/// engine defect and fails loudly, so it cannot silently pass a resource or inflate conformance
/// metrics.
/// </para>
/// </remarks>
public class FhirPathInvariantCheck : IValidationCheck
{
    private readonly IConstraint _constraint;
    private readonly ISchema _schema;
    private readonly FhirPathParser _parser;
    private readonly IReadOnlyList<string> _appliesTo;
    private readonly ILogger? _logger;
    private readonly Lazy<FhirPathEvaluator> _evaluator;
    private readonly Lazy<Func<InstanceCreationRequest, IElement?>> _instanceCreator;
    private readonly Lazy<FhirPath.Expressions.Expression?> _compiledExpression;

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

        // Instance selectors (Type { ... }) delegate object construction to a
        // schema-backed creator so created instances are first-class nodes.
        _instanceCreator = new Lazy<Func<InstanceCreationRequest, IElement?>>(() =>
            new SourceNodeInstanceFactory(_schema).Create);
        _compiledExpression = new Lazy<FhirPath.Expressions.Expression?>(() =>
        {
            try
            {
                return _parser.Parse(_constraint.Expression);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to parse FHIRPath expression for constraint {ConstraintKey} - constraint will not be evaluated", _constraint.Key);
                return null;
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

        // ele-1 ("must have @value or children") is an Element-sourced invariant that also lands on
        // the resource root, where its literal expression (hasValue() or children().count() > id.count())
        // rejects an otherwise-legal empty/near-empty resource (an empty Parameters, or a Patient
        // carrying only an id). The reference validator never fires ele-1 on the resource root itself —
        // a resource's presence is guaranteed — so exempt it here. The exemption keys off Scope.Resource,
        // and EnterContainedResource re-points Scope.Resource to the contained resource, so a contained
        // resource's own root is exempt on the same footing as the top-level root. Nested elements
        // (datatypes, backbones) still get ele-1; empty complex datatypes remain covered structurally
        // by StructuralShapeCheck.
        if (string.Equals(_constraint.Key, "ele-1", StringComparison.Ordinal)
            && ReferenceEquals(element, state.Scope.Resource))
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

        var expression = _compiledExpression.Value;
        if (expression is null)
        {
            var parseFailureIssue = new ValidationIssue(
                IssueSeverity.Warning,
                _constraint.Key,
                element.Location ?? string.Empty,
                $"Constraint '{_constraint.Key}' could not be evaluated: FHIRPath expression failed to parse");
            return new ValidationResult(isValid: true, issues: new[] { parseFailureIssue });
        }

        try
        {
            // Evaluate the FHIRPath expression, supplying %resource / %rootResource / resolve()
            // from the tree-context scope so root-referencing invariants (dom-*, bdl-*) evaluate
            // correctly. %context is bound from the node handed to the evaluator, which is the
            // constrained element - what the ~30 shipped %context invariants (ig-1, sdf-24/25,
            // exs-14..21) expect.
            var result = _evaluator.Value.Evaluate(element, expression, BuildEvaluationContext(state));

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
        catch (NotSupportedException ex)
        {
            // A KNOWN engine limitation — an unimplemented FHIRPath function (htmlChecks() /
            // conformsTo() / memberOf()) or operator, which the engine throws NotSupportedException
            // for. Not a resource error: degrade to a non-failing Warning, consistent with the
            // parse-failure path above, so we never reject a resource on our own engine gap.
            // (Reference validators likewise never hard-fail a resource on an unevaluable constraint.)
            _logger?.LogWarning(
                ex,
                "Constraint {ConstraintKey} uses an unsupported FHIRPath feature; treating as non-failing",
                _constraint.Key);

            var issue = new ValidationIssue(
                IssueSeverity.Warning,
                _constraint.Key,
                element.Location ?? string.Empty,
                $"Constraint '{_constraint.Key}' could not be evaluated: {ex.Message}");

            return new ValidationResult(isValid: true, issues: new[] { issue });
        }
        catch (FhirPathEvaluationException ex)
        {
            // A constraint the engine CORRECTLY REFUSED to evaluate — the expression is ill-formed for
            // the data it was handed, and FHIRPath requires signalling an error rather than yielding a
            // value. That is a defect in the constraint, not evidence that the resource is invalid, so it
            // gets the same non-failing Warning as the engine-gap path above. R4's tim-9 is the canonical
            // case: it feeds a repeating Timing.repeat.when to 'in', which is only defined for a singleton
            // (R5 rewrote it as when.select($this in (…)).allFalse() for exactly this reason). Failing the
            // resource here would reject a perfectly conformant instance for a bug in the spec's own text.
            _logger?.LogWarning(
                ex,
                "Constraint {ConstraintKey} is not evaluable against this instance; treating as non-failing",
                _constraint.Key);

            var issue = new ValidationIssue(
                IssueSeverity.Warning,
                _constraint.Key,
                element.Location ?? string.Empty,
                $"Constraint '{_constraint.Key}' could not be evaluated: {ex.Message}");

            return new ValidationResult(isValid: true, issues: new[] { issue });
        }
        catch (Exception ex)
        {
            // An UNEXPECTED evaluation failure — a defect in our engine or malformed data, not a known
            // limitation. Surface it loudly (failing Error + Error log) rather than masking it as a
            // benign warning, so it cannot silently pass a resource or inflate conformance metrics.
            _logger?.LogError(
                ex,
                "Unexpected error evaluating constraint {ConstraintKey}",
                _constraint.Key);

            var issue = new ValidationIssue(
                IssueSeverity.Error,
                _constraint.Key,
                element.Location ?? string.Empty,
                $"{_constraint.Key}: unexpected error evaluating FHIRPath expression: {ex.Message}");

            return ValidationResult.Failure(issue);
        }
    }

    /// <summary>
    /// Builds the FHIRPath evaluation context from the validation scope. Always carries the
    /// instance-creation delegate so instance selectors (<c>Type { ... }</c>) construct
    /// schema-backed nodes, and always carries the resource scope (%resource / %rootResource /
    /// resolve()) — <see cref="ValidationState"/> cannot exist without one. A fresh context is returned
    /// per evaluation because <see cref="EvaluationContext.DefinedVariables"/> is mutated by
    /// <c>defineVariable()</c>; sharing one instance would leak variables between constraints and race
    /// across threads.
    /// </summary>
    private EvaluationContext BuildEvaluationContext(ValidationState state)
    {
        var scope = state.Scope;

        // No ElementResolver: resolve() resolves in-instance from Resource/RootResource via
        // EvaluationContext.ReferenceIndexCache. ElementResolver is the seam for a HOST resolver
        // reaching OUTSIDE the instance, and validation has no such host - see ResourceScope.
        return new FhirEvaluationContext
        {
            Resource = scope.Resource,
            RootResource = scope.RootResource,
            InstanceCreator = _instanceCreator.Value,
            Schema = _schema
        };
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
