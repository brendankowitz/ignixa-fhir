// <copyright file="FastValidator.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation;

/// <summary>
/// Tier 1 (Fast) validator - validates basic structure and required fields.
/// Target: less than 25ms for typical resources.
/// Supports both universal checks (always run) and schema-driven checks (optional).
/// </summary>
public class FastValidator
{
    private readonly List<IValidationCheck> _checks;
    private readonly IValidationSchemaResolver? _schemaResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="FastValidator"/> class.
    /// Uses universal checks only (backward compatible).
    /// </summary>
    public FastValidator()
    {
        _checks = new List<IValidationCheck>
        {
            new JsonStructureCheck(),
            new IdFormatCheck(),
            new NarrativeCheck(),
            // Note: ReferenceFormatCheck and CodingStructureCheck are resource-specific
            // and should be added via schema-driven validation in Phase 3
        };
        _schemaResolver = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FastValidator"/> class with schema resolver.
    /// Uses universal checks + schema-specific checks (cardinality, required fields, types).
    /// </summary>
    /// <param name="schemaResolver">The schema resolver for resource-specific validation.</param>
    /// <exception cref="ArgumentNullException">Thrown if schemaResolver is null.</exception>
    public FastValidator(IValidationSchemaResolver schemaResolver)
    {
        _checks = new List<IValidationCheck>
        {
            new JsonStructureCheck(),
            new IdFormatCheck(),
            new NarrativeCheck(),
        };
        _schemaResolver = schemaResolver ?? throw new ArgumentNullException(nameof(schemaResolver));
    }

    /// <summary>
    /// Validates an ISourceNode at Tier 1 (Fast).
    /// Uses FHIR-aware navigation for choice types and shadow properties.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <returns>Validation result with any structural issues found.</returns>
    /// <example>
    /// <code>
    /// var json = JsonNode.Parse("{\"resourceType\":\"Patient\"}");
    /// var sourceNode = JsonNodeSourceNode.Create(json);
    /// var validator = new FastValidator();
    /// var result = validator.Validate(sourceNode);
    /// if (!result.IsValid) {
    ///     var outcome = result.ToOperationOutcome();
    /// }
    /// </code>
    /// </example>
    public ValidationResult Validate(ISourceNode node)
    {
        return ValidateSourceNode(node);
    }

    /// <summary>
    /// Validates with custom checks beyond the default Tier 1 checks.
    /// Useful for adding resource-specific validation logic.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="additionalChecks">Additional checks to run (e.g., CardinalityCheck, TypeCheck).</param>
    /// <returns>Combined validation result from all checks.</returns>
    /// <example>
    /// <code>
    /// var checks = new List&lt;IValidationCheck&gt;
    /// {
    ///     new RequiredFieldCheck("id", isRequired: true),
    ///     new CardinalityCheck("name", min: 1, max: null)
    /// };
    /// var result = validator.Validate(sourceNode, checks);
    /// </code>
    /// </example>
    public ValidationResult Validate(ISourceNode node, IEnumerable<IValidationCheck> additionalChecks)
    {
        var settings = new ValidationSettings { Tier = ValidationTier.Fast };
        var state = new ValidationState();
        var results = new List<ValidationResult>();

        var allChecks = _checks.Concat(additionalChecks);

        foreach (var check in allChecks)
        {
            results.Add(check.Validate(node, settings, state));
        }

        return ValidationResult.Combine(results);
    }

    private ValidationResult ValidateSourceNode(ISourceNode node)
    {
        var settings = new ValidationSettings { Tier = ValidationTier.Fast };
        var state = new ValidationState();
        var results = new List<ValidationResult>();

        // Run universal checks (always)
        foreach (var check in _checks)
        {
            results.Add(check.Validate(node, settings, state));
        }

        // If schema resolver available, run schema-specific checks
        if (_schemaResolver != null)
        {
            var resourceType = (node as IResourceTypeSupplier)?.ResourceType ?? node.Name;
            if (!string.IsNullOrEmpty(resourceType))
            {
                var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
                var schema = _schemaResolver.GetSchema(canonicalUrl);
                if (schema != null)
                {
                    results.Add(schema.Validate(node, settings, state));
                }
            }
        }

        return ValidationResult.Combine(results);
    }
}
