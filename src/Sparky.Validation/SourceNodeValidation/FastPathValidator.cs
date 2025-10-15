// <copyright file="FastPathValidator.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Sparky.SourceNodeSerialization.ElementModel;
using Sparky.SourceNodeSerialization.Specification;

namespace Sparky.Validation.SourceNodeValidation;

/// <summary>
/// Fast-path validator using ISourceNode for unified property access.
/// Performs lightweight validation (15-60ms estimated) before delegating to full Firely SDK validation.
/// Version-agnostic - works with any FHIR version (R4, R4B, R5, STU3).
/// Solves the missing property issue by using ISourceNode's unified view of all properties.
/// Thread-safe singleton that caches validation rules per (tenant, resourceType, provider) tuple.
/// Phase 1: Single-tenant mode (tenant is always TenantContext.Default).
/// Phase 2+: Multi-tenant mode with custom structure definitions per tenant.
/// </summary>
public sealed class FastPathValidator
{
    // Cache validation rules by (tenant, resourceType, provider identity)
    // Phase 1: Tenant is always TenantContext.Default
    // Phase 2+: Separate rules per tenant to support custom structure definitions
    private readonly ConcurrentDictionary<(Sparky.Extensions.TenantContext Tenant, string ResourceType, IStructureDefinitionSummaryProvider Provider), ValidationRuleSet> _ruleCache;

    // Regex patterns for primitive type validation
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"^\d{4}(-\d{2}(-\d{2})?)?$", RegexOptions.Compiled);
    private static readonly Regex DateTimePattern = new(@"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[\+\-]\d{2}:\d{2})?)?)?)?$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"^\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="FastPathValidator"/> class.
    /// </summary>
    public FastPathValidator()
    {
        _ruleCache = new ConcurrentDictionary<(Sparky.Extensions.TenantContext, string, IStructureDefinitionSummaryProvider), ValidationRuleSet>();
    }

    /// <summary>
    /// Validates a resource using ISourceNode with version-specific schema provider.
    /// Fast: O(n) where n = number of elements, typically 15-60ms.
    /// Thread-safe - caches rules per (resourceType, provider) pair.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="provider">Version-specific structure definition provider (from FhirVersionContext).</param>
    /// <returns>A validation result with any issues found.</returns>
    public ValidationResult Validate(ISourceNode node, IStructureDefinitionSummaryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(provider);

        // Get resourceType from IResourceTypeSupplier annotation or Name
        string? resourceType = (node as IResourceTypeSupplier)?.ResourceType ?? node.Name;

        if (string.IsNullOrEmpty(resourceType))
        {
            return ValidationResult.Failure(new ValidationIssue(
                IssueSeverity.Error,
                "resourceType",
                "Resource must have a resourceType property"));
        }

        // Get or build cached validation rules for this (tenant, resourceType, provider) combination
        // Phase 1: Always use TenantContext.Default for single-tenant mode
        // Phase 2+: Extract tenant from HttpContext for multi-tenant mode
        var tenant = Sparky.Extensions.TenantContext.Default;
        var cacheKey = (tenant, resourceType, provider);
        if (!_ruleCache.TryGetValue(cacheKey, out var rules))
        {
            var newRules = BuildValidationRules(resourceType, provider);
            if (newRules is null)
            {
                return ValidationResult.Failure(new ValidationIssue(
                    IssueSeverity.Error,
                    "resourceType",
                    $"Unknown resource type: {resourceType}"));
            }

            rules = _ruleCache.GetOrAdd(cacheKey, newRules);
        }

        var issues = new List<ValidationIssue>();

        // 1. Required elements validation
        ValidateRequiredElements(node, rules.RequiredElements, issues);

        // 2. Cardinality validation
        ValidateCardinality(node, rules.CardinalityRules, issues);

        // 3. ID format validation
        ValidateIdFormat(node, issues);

        // 4. Reference format validation
        ValidateReferenceFormat(node, rules.ReferenceFields, issues);

        // 5. Reference targets validation (using Phase 4 metadata)
        ValidateReferenceTargets(node, rules.ReferenceTargetRules, issues);

        // 6. Primitive type formats validation
        ValidatePrimitiveFormats(node, rules.PrimitiveFormatRules, issues);

        // 7. Coding structure validation
        ValidateCodingStructure(node, rules.CodingFields, issues);

        // 8. Narrative basics validation
        ValidateNarrativeBasics(node, issues);

        // Determine if valid (no errors or fatal issues)
        bool isValid = !issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);

        return new ValidationResult(isValid, issues);
    }

    /// <summary>
    /// Builds validation rules from IStructureDefinitionSummaryProvider metadata.
    /// Called once per (resourceType, provider) combination, then cached.
    /// </summary>
    private ValidationRuleSet? BuildValidationRules(string resourceType, IStructureDefinitionSummaryProvider provider)
    {
        var summary = provider.Provide(resourceType);
        if (summary is null)
        {
            return null;
        }

        var elements = summary.GetElements();

        return new ValidationRuleSet
        {
            // Required elements: Min > 0 (IsRequired)
            RequiredElements = elements
                .Where(e => e.IsRequired)
                .Select(e => new RequiredElementRule(e.ElementName))
                .ToList(),

            // Cardinality rules: Min/Max from metadata
            CardinalityRules = elements
                .Select(e => new CardinalityRule(
                    e.ElementName,
                    e.IsRequired ? 1 : 0,
                    e.IsCollection ? (int?)null : 1))
                .ToList(),

            // Type rules
            TypeRules = elements
                .Select(e => new TypeRule(
                    e.ElementName,
                    e.Type.Select(t => (t as IStructureDefinitionSummary)?.TypeName ?? e.DefaultTypeName).Where(t => t != null).ToArray()!,
                    e.IsChoiceElement))
                .ToList(),

            // Reference fields
            ReferenceFields = elements
                .Where(e => e.DefaultTypeName == "Reference")
                .Select(e => e.ElementName)
                .ToList(),

            // Reference target rules (using Phase 4 metadata)
            ReferenceTargetRules = elements
                .Where(e => e.DefaultTypeName == "Reference")
                .Select(e =>
                {
                    // Access extended metadata if available
                    // Note: The generated code already has ReferenceTargets as public properties
                    // We just need to access them via reflection or casting until regeneration
                    // For now, return empty rule - will be populated after regeneration
                    return new ReferenceTargetRule(e.ElementName, Array.Empty<string>());
                })
                .Where(r => r.AllowedTargets.Length > 0)
                .ToList(),

            // Primitive format rules
            PrimitiveFormatRules = elements
                .Where(e => e.DefaultTypeName != null && IsPrimitiveType(e.DefaultTypeName))
                .Select(e => new PrimitiveFormatRule(e.ElementName, e.DefaultTypeName!))
                .ToList(),

            // Coding fields (CodeableConcept, Coding)
            CodingFields = elements
                .Where(e => e.DefaultTypeName is "CodeableConcept" or "Coding")
                .Select(e => e.ElementName)
                .ToList(),

            // Choice type rules
            ChoiceTypeRules = elements
                .Where(e => e.IsChoiceElement)
                .Select(e => new ChoiceTypeRule(
                    e.ElementName,
                    e.Type.Select(t => (t as IStructureDefinitionSummary)?.TypeName ?? e.DefaultTypeName).Where(t => t != null).ToArray()!))
                .ToList(),
        };
    }

    private static bool IsPrimitiveType(string typeName) =>
        typeName switch
        {
            "id" or "string" or "uri" or "url" or "canonical" or
            "date" or "dateTime" or "instant" or "time" or
            "boolean" or "integer" or "decimal" or "positiveInt" or
            "unsignedInt" or "code" or "oid" or "uuid" => true,
            _ => false,
        };

    private void ValidateRequiredElements(
        ISourceNode node,
        IReadOnlyList<RequiredElementRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            if (!node.Children(rule.Path).Any())
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    rule.Path,
                    $"Required element '{rule.Path}' is missing"));
            }
        }
    }

    private void ValidateCardinality(
        ISourceNode node,
        IReadOnlyList<CardinalityRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            var children = node.Children(rule.Path).ToList();

            if (children.Count == 0)
            {
                // Element not present - min cardinality check
                if (rule.Min > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        rule.Path,
                        $"Element '{rule.Path}' requires minimum {rule.Min} occurrence(s)"));
                }

                continue;
            }

            int count = children.Count;

            if (count < rule.Min)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    rule.Path,
                    $"Element '{rule.Path}' requires minimum {rule.Min} occurrence(s), found {count}"));
            }

            if (rule.Max.HasValue && count > rule.Max.Value)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    rule.Path,
                    $"Element '{rule.Path}' allows maximum {rule.Max} occurrence(s), found {count}"));
            }
        }
    }

    private void ValidateIdFormat(ISourceNode node, List<ValidationIssue> issues)
    {
        var idNode = node.Children("id").FirstOrDefault();
        if (idNode is not null)
        {
            string? id = idNode.Text;
            if (!string.IsNullOrEmpty(id) && !IdPattern.IsMatch(id))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "id",
                    $"Resource ID '{id}' is not valid. Must match pattern: [A-Za-z0-9\\-\\.]{{1,64}}"));
            }
        }
    }

    private void ValidateReferenceFormat(
        ISourceNode node,
        IReadOnlyList<string> referenceFields,
        List<ValidationIssue> issues)
    {
        foreach (var field in referenceFields)
        {
            var fieldNodes = node.Children(field);
            foreach (var fieldNode in fieldNodes)
            {
                ValidateReferenceNode(field, fieldNode, issues);
            }
        }
    }

    private void ValidateReferenceNode(
        string path,
        ISourceNode referenceNode,
        List<ValidationIssue> issues)
    {
        var referenceChild = referenceNode.Children("reference").FirstOrDefault();
        if (referenceChild is not null)
        {
            string? reference = referenceChild.Text;
            if (!string.IsNullOrEmpty(reference) && !IsValidReferenceFormat(reference))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"{path}.reference",
                    $"Reference '{reference}' is not a valid FHIR reference format"));
            }
        }
    }

    private static bool IsValidReferenceFormat(string reference)
    {
        // Valid formats:
        // - ResourceType/id
        // - http(s)://server/ResourceType/id
        // - urn:uuid:...
        // - #fragment
        if (reference.StartsWith('#'))
        {
            return true; // Fragment reference
        }

        if (reference.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            return true; // UUID reference
        }

        if (reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Absolute URL - should have ResourceType/id at the end
            var parts = reference.Split('/');
            return parts.Length >= 2; // At minimum: http://server/Type/id
        }

        // Relative reference: ResourceType/id
        var segments = reference.Split('/');
        return segments.Length == 2 && !string.IsNullOrEmpty(segments[0]) && !string.IsNullOrEmpty(segments[1]);
    }

    private void ValidateReferenceTargets(
        ISourceNode node,
        IReadOnlyList<ReferenceTargetRule> rules,
        List<ValidationIssue> issues)
    {
        // TODO: Implement after regeneration with IExtendedElementMetadata
        // For now, skip this validation as we don't have access to ReferenceTargets metadata yet
    }

    private void ValidatePrimitiveFormats(
        ISourceNode node,
        IReadOnlyList<PrimitiveFormatRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            var children = node.Children(rule.Path);
            foreach (var child in children)
            {
                string? value = child.Text;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                string? errorMessage = rule.PrimitiveType switch
                {
                    "id" => !IdPattern.IsMatch(value) ? $"Invalid id format: '{value}'" : null,
                    "date" => !DatePattern.IsMatch(value) ? $"Invalid date format: '{value}'. Expected YYYY-MM-DD" : null,
                    "dateTime" => !DateTimePattern.IsMatch(value) ? $"Invalid dateTime format: '{value}'" : null,
                    "time" => !TimePattern.IsMatch(value) ? $"Invalid time format: '{value}'. Expected HH:MM:SS" : null,
                    "boolean" => value is not "true" and not "false" ? $"Invalid boolean value: '{value}'. Expected 'true' or 'false'" : null,
                    _ => null, // Other primitive types not validated yet
                };

                if (errorMessage is not null)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Error, rule.Path, errorMessage));
                }
            }
        }
    }

    private void ValidateCodingStructure(
        ISourceNode node,
        IReadOnlyList<string> codingFields,
        List<ValidationIssue> issues)
    {
        foreach (var field in codingFields)
        {
            var fieldNodes = node.Children(field);
            foreach (var fieldNode in fieldNodes)
            {
                // Check if this is a CodeableConcept (has 'coding' child) or a Coding directly
                var codingChildren = fieldNode.Children("coding").ToList();
                if (codingChildren.Count > 0)
                {
                    // This is a CodeableConcept - validate each Coding in the array
                    foreach (var coding in codingChildren)
                    {
                        ValidateSingleCoding($"{field}.coding", coding, issues);
                    }
                }
                else
                {
                    // This is a Coding directly
                    ValidateSingleCoding(field, fieldNode, issues);
                }
            }
        }
    }

    private void ValidateSingleCoding(
        string path,
        ISourceNode codingNode,
        List<ValidationIssue> issues)
    {
        bool hasSystem = codingNode.Children("system").Any();
        bool hasCode = codingNode.Children("code").Any();

        if (!hasSystem && !hasCode)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                path,
                "Coding should have at least a system or code"));
        }
    }

    private void ValidateNarrativeBasics(ISourceNode node, List<ValidationIssue> issues)
    {
        var textNode = node.Children("text").FirstOrDefault();
        if (textNode is null)
        {
            return; // No narrative present (optional)
        }

        // Check for status field (required if text present)
        var statusNode = textNode.Children("status").FirstOrDefault();
        if (statusNode is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.status",
                "Narrative must have a status field"));
            return;
        }

        string? status = statusNode.Text;
        if (status is not ("generated" or "extensions" or "additional" or "empty"))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.status",
                $"Invalid narrative status: '{status}'. Must be one of: generated, extensions, additional, empty"));
        }

        // Check for div field (required if status is not 'empty')
        if (status != "empty" && !textNode.Children("div").Any())
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.div",
                "Narrative must have a div field when status is not 'empty'"));
        }
    }
}
