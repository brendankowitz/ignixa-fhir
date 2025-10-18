// <copyright file="FastPathValidator.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;
using Ignixa.SourceNodeSerialization.Specification;

namespace Ignixa.Validation.JsonNodeValidation;

/// <summary>
/// Fast-path validator using generated IStructureDefinitionSummaryProvider metadata.
/// Performs lightweight validation (10-50ms) before delegating to full Firely SDK validation.
/// Version-agnostic - works with any FHIR version (R4, R4B, R5, STU3).
/// </summary>
public sealed class FastPathValidator
{
    private readonly IStructureDefinitionSummaryProvider _provider;
    private readonly ConcurrentDictionary<string, ValidationRuleSet> _ruleCache;

    // Regex patterns for primitive type validation
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"^\d{4}(-\d{2}(-\d{2})?)?$", RegexOptions.Compiled);
    private static readonly Regex DateTimePattern = new(@"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[\+\-]\d{2}:\d{2})?)?)?)?$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"^\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="FastPathValidator"/> class.
    /// </summary>
    /// <param name="provider">The structure definition provider for metadata.</param>
    public FastPathValidator(IStructureDefinitionSummaryProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ruleCache = new ConcurrentDictionary<string, ValidationRuleSet>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Validates a resource using its JSON representation.
    /// Fast: O(n) where n = number of elements, typically 10-50ms.
    /// </summary>
    /// <param name="resource">The resource to validate.</param>
    /// <returns>A validation result with any issues found.</returns>
    public ValidationResult Validate(ResourceJsonNode resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (string.IsNullOrEmpty(resource.ResourceType))
        {
            return ValidationResult.Failure(new ValidationIssue(
                IssueSeverity.Error,
                "resourceType",
                "Resource must have a resourceType property"));
        }

        // Get or build cached validation rules for this resource type
        if (!_ruleCache.TryGetValue(resource.ResourceType, out var rules))
        {
            var newRules = BuildValidationRules(resource.ResourceType);
            if (newRules is null)
            {
                return ValidationResult.Failure(new ValidationIssue(
                    IssueSeverity.Error,
                    "resourceType",
                    $"Unknown resource type: {resource.ResourceType}"));
            }

            rules = _ruleCache.GetOrAdd(resource.ResourceType, newRules);
        }

        var issues = new List<ValidationIssue>();

        // 1. Required elements validation
        ValidateRequiredElements(resource, rules.RequiredElements, issues);

        // 2. Cardinality validation
        ValidateCardinality(resource, rules.CardinalityRules, issues);

        // 3. ID format validation
        ValidateIdFormat(resource, issues);

        // 4. Reference format validation
        ValidateReferenceFormat(resource, rules.ReferenceFields, issues);

        // 5. Reference targets validation (using Phase 4 metadata)
        ValidateReferenceTargets(resource, rules.ReferenceTargetRules, issues);

        // 6. Primitive type formats validation
        ValidatePrimitiveFormats(resource, rules.PrimitiveFormatRules, issues);

        // 7. Coding structure validation
        ValidateCodingStructure(resource, rules.CodingFields, issues);

        // 8. Narrative basics validation
        ValidateNarrativeBasics(resource, issues);

        // Determine if valid (no errors or fatal issues)
        bool isValid = !issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);

        return new ValidationResult(isValid, issues);
    }

    /// <summary>
    /// Builds validation rules from IStructureDefinitionSummaryProvider metadata.
    /// Called once per resource type, then cached forever.
    /// </summary>
    private ValidationRuleSet? BuildValidationRules(string resourceType)
    {
        var summary = _provider.Provide(resourceType);
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
        ResourceJsonNode resource,
        IReadOnlyList<RequiredElementRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            if (!resource.MutableNode.ContainsKey(rule.Path))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    rule.Path,
                    $"Required element '{rule.Path}' is missing"));
            }
        }
    }

    private void ValidateCardinality(
        ResourceJsonNode resource,
        IReadOnlyList<CardinalityRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            if (!resource.MutableNode.TryGetPropertyValue(rule.Path, out var element))
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

            // Check if it's an array
            if (element is JsonArray array)
            {
                int count = array.Count;

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
            else
            {
                // Single value - check max cardinality
                if (rule.Max.HasValue && rule.Max.Value < 1)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        rule.Path,
                        $"Element '{rule.Path}' does not allow values (max cardinality 0)"));
                }
            }
        }
    }

    private void ValidateIdFormat(ResourceJsonNode resource, List<ValidationIssue> issues)
    {
        if (resource.MutableNode.TryGetPropertyValue("id", out var idElement) && idElement != null)
        {
            string? id = idElement.GetValue<string>();
            if (id is not null && !IdPattern.IsMatch(id))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "id",
                    $"Resource ID '{id}' is not valid. Must match pattern: [A-Za-z0-9\\-\\.]{{1,64}}"));
            }
        }
    }

    private void ValidateReferenceFormat(
        ResourceJsonNode resource,
        IReadOnlyList<string> referenceFields,
        List<ValidationIssue> issues)
    {
        foreach (var field in referenceFields)
        {
            if (!resource.MutableNode.TryGetPropertyValue(field, out var refElement))
            {
                continue;
            }

            // Handle both single reference and array of references
            ValidateReferenceElement(field, refElement, issues);
        }
    }

    private void ValidateReferenceElement(
        string path,
        JsonNode? element,
        List<ValidationIssue> issues)
    {
        if (element is JsonArray array)
        {
            int index = 0;
            foreach (var item in array)
            {
                ValidateSingleReference($"{path}[{index}]", item, issues);
                index++;
            }
        }
        else if (element is JsonObject)
        {
            ValidateSingleReference(path, element, issues);
        }
    }

    private void ValidateSingleReference(
        string path,
        JsonNode? refObject,
        List<ValidationIssue> issues)
    {
        if (refObject is JsonObject obj &&
            obj.TryGetPropertyValue("reference", out var refValue) &&
            refValue != null)
        {
            string? reference = refValue.GetValue<string>();
            if (reference is not null && !IsValidReferenceFormat(reference))
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
        ResourceJsonNode resource,
        IReadOnlyList<ReferenceTargetRule> rules,
        List<ValidationIssue> issues)
    {
        // TODO: Implement after regeneration with IExtendedElementMetadata
        // For now, skip this validation as we don't have access to ReferenceTargets metadata yet
    }

    private void ValidatePrimitiveFormats(
        ResourceJsonNode resource,
        IReadOnlyList<PrimitiveFormatRule> rules,
        List<ValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            if (!resource.MutableNode.TryGetPropertyValue(rule.Path, out var element) || element == null)
            {
                continue;
            }

            // Try to get as string value
            string? value = null;
            try
            {
                value = element.GetValue<string>();
            }
            catch
            {
                continue; // Not a string, type validation will catch this
            }

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

    private void ValidateCodingStructure(
        ResourceJsonNode resource,
        IReadOnlyList<string> codingFields,
        List<ValidationIssue> issues)
    {
        foreach (var field in codingFields)
        {
            if (!resource.MutableNode.TryGetPropertyValue(field, out var element))
            {
                continue;
            }

            if (element is JsonObject obj)
            {
                // Could be a Coding or CodeableConcept
                // CodeableConcept has a 'coding' array property
                if (obj.TryGetPropertyValue("coding", out var codingArray) &&
                    codingArray is JsonArray codingArrayNode)
                {
                    // This is a CodeableConcept - validate each Coding in the array
                    int index = 0;
                    foreach (var item in codingArrayNode)
                    {
                        if (item is JsonObject)
                        {
                            ValidateSingleCoding($"{field}.coding[{index}]", item, issues);
                        }

                        index++;
                    }
                }
                else
                {
                    // This is a Coding directly
                    ValidateSingleCoding(field, obj, issues);
                }
            }
            else if (element is JsonArray array)
            {
                // Array of Coding objects
                int index = 0;
                foreach (var item in array)
                {
                    if (item is JsonObject)
                    {
                        ValidateSingleCoding($"{field}[{index}]", item, issues);
                    }

                    index++;
                }
            }
        }
    }

    private void ValidateSingleCoding(
        string path,
        JsonNode? codingObject,
        List<ValidationIssue> issues)
    {
        if (codingObject is not JsonObject obj)
        {
            return;
        }

        bool hasSystem = obj.TryGetPropertyValue("system", out _);
        bool hasCode = obj.TryGetPropertyValue("code", out _);

        if (!hasSystem && !hasCode)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                path,
                "Coding should have at least a system or code"));
        }
    }

    private void ValidateNarrativeBasics(ResourceJsonNode resource, List<ValidationIssue> issues)
    {
        if (!resource.MutableNode.TryGetPropertyValue("text", out var textElement))
        {
            return; // No narrative present (optional)
        }

        if (textElement is not JsonObject textObj)
        {
            return;
        }

        // Check for status field (required if text present)
        if (!textObj.TryGetPropertyValue("status", out var statusElement) || statusElement == null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.status",
                "Narrative must have a status field"));
            return;
        }

        string? status = statusElement.GetValue<string>();
        if (status is not ("generated" or "extensions" or "additional" or "empty"))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.status",
                $"Invalid narrative status: '{status}'. Must be one of: generated, extensions, additional, empty"));
        }

        // Check for div field (required if status is not 'empty')
        if (status != "empty" && !textObj.TryGetPropertyValue("div", out _))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "text.div",
                "Narrative must have a div field when status is not 'empty'"));
        }
    }
}
