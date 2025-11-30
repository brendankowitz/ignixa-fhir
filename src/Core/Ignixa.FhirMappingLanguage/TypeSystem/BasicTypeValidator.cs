/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Basic type validator for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.TypeSystem;

/// <summary>
/// Basic implementation of type validation for mapping language.
/// Validates primitive types and basic type compatibility.
/// </summary>
internal class BasicTypeValidator : ITypeValidator
{
    // FHIR primitive types
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "base64Binary", "boolean", "canonical", "code", "date", "dateTime",
        "decimal", "id", "instant", "integer", "integer64", "markdown",
        "oid", "positiveInt", "string", "time", "unsignedInt", "uri", "url", "uuid"
    };

    // Common FHIR complex types
    private static readonly HashSet<string> ComplexTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Address", "Age", "Annotation", "Attachment", "CodeableConcept", "Coding",
        "ContactPoint", "Count", "Distance", "Duration", "HumanName", "Identifier",
        "Money", "Period", "Quantity", "Range", "Ratio", "Reference", "SampledData",
        "Signature", "Timing", "ContactDetail", "Contributor", "DataRequirement",
        "Expression", "ParameterDefinition", "RelatedArtifact", "TriggerDefinition",
        "UsageContext", "Dosage", "Meta"
    };

    // Common FHIR resource types
    private static readonly HashSet<string> ResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Patient", "Observation", "Practitioner", "Organization", "Encounter",
        "Condition", "Procedure", "MedicationRequest", "DiagnosticReport",
        "AllergyIntolerance", "CarePlan", "CareTeam", "Claim", "Coverage",
        "Device", "DocumentReference", "Goal", "Immunization", "Location",
        "Medication", "Specimen", "Bundle", "OperationOutcome", "Parameters",
        "StructureDefinition", "StructureMap", "ValueSet", "CodeSystem", "ConceptMap"
    };

    private readonly Dictionary<string, TypeInfo> _typeCache = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<TypeValidationError> ValidateMap(MapExpression map)
    {
        List<TypeValidationError> errors = [];

        // Build a map of declared types from "uses" declarations
        var declaredTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var use in map.Uses)
        {
            if (use.Alias != null)
            {
                // Extract type name from StructureDefinition URL
                var typeName = ExtractTypeNameFromUrl(use.Url);
                if (typeName != null)
                {
                    declaredTypes[use.Alias] = typeName;
                }
            }
        }

        // Validate each group
        foreach (var group in map.Groups)
        {
            errors.AddRange(ValidateGroup(group, declaredTypes));
        }

        return errors;
    }

    public bool IsTypeCompatible(string sourceType, string targetType)
    {
        // Exact match
        if (string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Resolve types
        var sourceInfo = ResolveType(sourceType);
        var targetInfo = ResolveType(targetType);

        if (sourceInfo == null || targetInfo == null)
        {
            // If we can't resolve, be permissive (for now)
            return true;
        }

        // Same category is generally compatible
        if (sourceInfo.Category == targetInfo.Category)
        {
            return true;
        }

        // Specific compatibility rules
        return IsCompatibleByRules(sourceInfo, targetInfo);
    }

    public TypeInfo? ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        // Check cache
        if (_typeCache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        // Determine type category
        TypeInfo typeInfo;
        if (PrimitiveTypes.Contains(typeName))
        {
            typeInfo = new TypeInfo(typeName, TypeCategory.Primitive);
        }
        else if (ResourceTypes.Contains(typeName))
        {
            typeInfo = new TypeInfo(typeName, TypeCategory.Resource, "Resource");
        }
        else if (ComplexTypes.Contains(typeName))
        {
            typeInfo = new TypeInfo(typeName, TypeCategory.Complex, "Element");
        }
        else
        {
            // Unknown type - could be a custom profile or extension
            typeInfo = new TypeInfo(typeName, TypeCategory.Unknown);
        }

        // Cache and return
        _typeCache[typeName] = typeInfo;
        return typeInfo;
    }

    public TypeValidationError? ValidateElement(IElement element, string expectedType)
    {
        if (element == null)
        {
            return new TypeValidationError($"Element is null, expected type '{expectedType}'");
        }

        var actualType = element.InstanceType;
        if (string.IsNullOrEmpty(actualType))
        {
            return new TypeValidationError($"Element has no type information, expected type '{expectedType}'");
        }

        if (!IsTypeCompatible(actualType, expectedType))
        {
            return new TypeValidationError(
                $"Type mismatch: element has type '{actualType}' but expected '{expectedType}'");
        }

        return null;
    }

    #region Private Helper Methods

    private IEnumerable<TypeValidationError> ValidateGroup(GroupExpression group, Dictionary<string, string> declaredTypes)
    {
        List<TypeValidationError> errors = [];

        // Validate parameter types
        foreach (var param in group.Parameters)
        {
            if (param.Type != null)
            {
                // Check if type is declared in uses
                if (declaredTypes.TryGetValue(param.Type, out var declaredType))
                {
                    // Type is declared, validate it's a known type
                    var typeInfo = ResolveType(declaredType);
                    if (typeInfo?.Category == TypeCategory.Unknown)
                    {
                        errors.Add(new TypeValidationError(
                            $"Parameter '{param.Name}' has unknown type '{declaredType}'",
                            param.Location));
                    }
                }
                else
                {
                    // Type is not an alias, validate it directly
                    var typeInfo = ResolveType(param.Type);
                    if (typeInfo?.Category == TypeCategory.Unknown)
                    {
                        errors.Add(new TypeValidationError(
                            $"Parameter '{param.Name}' has unknown type '{param.Type}'",
                            param.Location));
                    }
                }
            }
        }

        // Validate rules within the group
        foreach (var rule in group.Rules)
        {
            errors.AddRange(ValidateRule(rule, declaredTypes));
        }

        return errors;
    }

    private IEnumerable<TypeValidationError> ValidateRule(RuleExpression rule, Dictionary<string, string> declaredTypes)
    {
        List<TypeValidationError> errors = [];

        // Validate source type annotations
        foreach (var source in rule.Sources)
        {
            if (source.Type != null)
            {
                var typeInfo = ResolveType(source.Type);
                if (typeInfo?.Category == TypeCategory.Unknown)
                {
                    errors.Add(new TypeValidationError(
                        $"Source has unknown type '{source.Type}'",
                        source.Location));
                }
            }
        }

        // Validate transforms in targets
        foreach (var target in rule.Targets)
        {
            if (target.Transform is TransformExpression transform)
            {
                errors.AddRange(ValidateTransform(transform));
            }
        }

        // Validate dependent expression (group invocation or nested rules)
        if (rule.Dependent != null)
        {
            errors.AddRange(ValidateDependentExpression(rule.Dependent, declaredTypes));
        }

        return errors;
    }

    private IEnumerable<TypeValidationError> ValidateDependentExpression(Expression dependent, Dictionary<string, string> declaredTypes)
    {
        List<TypeValidationError> errors = [];

        switch (dependent)
        {
            case RuleSetExpression ruleSet:
                // Validate nested rules
                foreach (var nestedRule in ruleSet.Rules)
                {
                    errors.AddRange(ValidateRule(nestedRule, declaredTypes));
                }
                break;

            case GroupInvocationExpression:
                // Group invocations are validated at runtime
                // No type validation needed here
                break;

            default:
                errors.Add(new TypeValidationError(
                    $"Unknown dependent expression type: {dependent.GetType().Name}",
                    dependent.Location));
                break;
        }

        return errors;
    }

    private IEnumerable<TypeValidationError> ValidateTransform(TransformExpression transform)
    {
        List<TypeValidationError> errors = [];

        // Validate transform function exists (handled by StandardTransforms)
        // Here we can add additional type-specific validation if needed

        // For "create" transform, validate the type argument
        if (transform.FunctionName.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            if (transform.Arguments.Count > 0)
            {
                var typeArg = transform.Arguments[0];
                if (typeArg is LiteralExpression literal && literal.Value is string typeName)
                {
                    var typeInfo = ResolveType(typeName);
                    if (typeInfo?.Category == TypeCategory.Unknown)
                    {
                        errors.Add(new TypeValidationError(
                            $"create() function references unknown type '{typeName}'",
                            transform.Location));
                    }
                    else if (typeInfo?.Category == TypeCategory.Primitive)
                    {
                        errors.Add(new TypeValidationError(
                            $"create() function cannot create primitive type '{typeName}'",
                            transform.Location));
                    }
                }
            }
        }

        return errors;
    }

    private string? ExtractTypeNameFromUrl(string url)
    {
        // Extract type name from StructureDefinition URL
        // Examples:
        // "http://hl7.org/fhir/StructureDefinition/Patient" -> "Patient"
        // "http://hl7.org/fhir/StructureDefinition/HumanName" -> "HumanName"

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var lastSlashIndex = url.LastIndexOf('/');
        if (lastSlashIndex >= 0 && lastSlashIndex < url.Length - 1)
        {
            return url.Substring(lastSlashIndex + 1);
        }

        return null;
    }

    private bool IsCompatibleByRules(TypeInfo sourceInfo, TypeInfo targetInfo)
    {
        // Integer can convert to decimal
        if (sourceInfo.Name.Equals("integer", StringComparison.OrdinalIgnoreCase) &&
            targetInfo.Name.Equals("decimal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // String can convert to code/id/uri/url
        if (sourceInfo.Name.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            if (targetInfo.Name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                targetInfo.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                targetInfo.Name.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
                targetInfo.Name.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Code/id/uri/url can convert to string
        if (targetInfo.Name.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceInfo.Name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                sourceInfo.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                sourceInfo.Name.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
                sourceInfo.Name.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // All resources are compatible with Resource base type
        if (sourceInfo.IsResource && targetInfo.Name.Equals("Resource", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    #endregion
}
