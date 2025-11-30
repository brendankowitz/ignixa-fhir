/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Validator for FHIR Mapping Language mappings.
 */

using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;

namespace Ignixa.FhirMappingLanguage.Validation;

/// <summary>
/// Validates FHIR Mapping Language mappings without executing transformations.
/// </summary>
internal class MappingValidator
{
    private readonly ImportResolver? _importResolver;

    public MappingValidator(ImportResolver? importResolver = null)
    {
        _importResolver = importResolver;
    }

    /// <summary>
    /// Validates a mapping without executing it.
    /// </summary>
    /// <param name="map">The mapping to validate</param>
    /// <returns>Validation result with errors and warnings</returns>
    public ValidationResult Validate(MapExpression map)
    {
        var result = new ValidationResult();

        try
        {
            // Validate map structure
            ValidateMapStructure(map, result);

            // Validate each group
            foreach (var group in map.Groups)
            {
                ValidateGroup(map, group, result);
            }

            // Validate imports
            ValidateImports(map, result);
        }
        catch (Exception ex)
        {
            result.AddError($"Unexpected error during validation: {ex.Message}", code: "VALIDATION_ERROR");
        }

        return result;
    }

    private void ValidateMapStructure(MapExpression map, ValidationResult result)
    {
        // Validate URL
        if (string.IsNullOrWhiteSpace(map.Url))
        {
            result.AddError("Map URL is required", location: "Map", code: "MISSING_URL");
        }

        // Validate ID
        if (string.IsNullOrWhiteSpace(map.Identifier))
        {
            result.AddError("Map ID is required", location: "Map", code: "MISSING_ID");
        }

        // Validate at least one group
        if (map.Groups.Count == 0)
        {
            result.AddError("Map must contain at least one group", location: "Map", code: "NO_GROUPS");
        }

        // Check for duplicate group names
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in map.Groups)
        {
            if (!groupNames.Add(group.Name))
            {
                result.AddError($"Duplicate group name: {group.Name}", location: "Map", code: "DUPLICATE_GROUP");
            }
        }
    }

    private void ValidateGroup(MapExpression map, GroupExpression group, ValidationResult result)
    {
        var location = $"Group: {group.Name}";

        // Validate group name
        if (string.IsNullOrWhiteSpace(group.Name))
        {
            result.AddError("Group name is required", location: location, code: "MISSING_GROUP_NAME");
        }

        // Validate parameters
        if (group.Parameters.Count == 0)
        {
            result.AddWarning("Group has no parameters", location: location, code: "NO_PARAMETERS");
        }

        // Validate parameter types
        foreach (var param in group.Parameters)
        {
            if (string.IsNullOrWhiteSpace(param.Type))
            {
                result.AddWarning($"Parameter '{param.Name}' has no type specified", location: location, code: "NO_PARAMETER_TYPE");
            }
        }

        // Validate extends
        if (!string.IsNullOrWhiteSpace(group.Extends))
        {
            ValidateExtends(map, group, result);
        }

        // Validate rules
        if (group.Rules.Count == 0)
        {
            result.AddWarning($"Group '{group.Name}' has no rules", location: location, code: "NO_RULES");
        }

        for (int i = 0; i < group.Rules.Count; i++)
        {
            ValidateRule(map, group, group.Rules[i], i, result);
        }
    }

    private void ValidateExtends(MapExpression map, GroupExpression group, ValidationResult result)
    {
        var location = $"Group: {group.Name}";

        // Check if base group exists
        GroupExpression? baseGroup = null;

        // First check in current map
        baseGroup = map.Groups.FirstOrDefault(g =>
            g.Name.Equals(group.Extends, StringComparison.OrdinalIgnoreCase));

        // Then check imports if resolver available
        if (baseGroup == null && _importResolver != null)
        {
            baseGroup = _importResolver.FindGroup(map, group.Extends!);
        }

        if (baseGroup == null)
        {
            result.AddError($"Base group '{group.Extends}' not found", location: location, code: "MISSING_BASE_GROUP");
            return;
        }

        // Check for circular inheritance
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { group.Name };
        var current = baseGroup;

        while (current != null && !string.IsNullOrWhiteSpace(current.Extends))
        {
            if (!visited.Add(current.Extends))
            {
                result.AddError($"Circular inheritance detected in group '{group.Name}'", location: location, code: "CIRCULAR_INHERITANCE");
                break;
            }

            current = map.Groups.FirstOrDefault(g =>
                g.Name.Equals(current.Extends, StringComparison.OrdinalIgnoreCase));

            if (current == null && _importResolver != null)
            {
                current = _importResolver.FindGroup(map, current?.Extends ?? string.Empty);
            }
        }
    }

    private void ValidateRule(MapExpression map, GroupExpression group, RuleExpression rule, int ruleIndex, ValidationResult result)
    {
        var location = !string.IsNullOrWhiteSpace(rule.Name)
            ? $"Group: {group.Name}, Rule: {rule.Name}"
            : $"Group: {group.Name}, Rule #{ruleIndex + 1}";

        // Validate sources
        if (rule.Sources.Count == 0)
        {
            result.AddError("Rule must have at least one source", location: location, code: "NO_SOURCES");
        }

        foreach (var source in rule.Sources)
        {
            ValidateSource(source, location, result);
        }

        // Validate targets
        if (rule.Targets.Count == 0)
        {
            result.AddWarning("Rule has no targets", location: location, code: "NO_TARGETS");
        }

        foreach (var target in rule.Targets)
        {
            ValidateTarget(target, location, result);
        }

        // Validate dependent expression (group invocation or nested rules)
        if (rule.Dependent != null)
        {
            ValidateDependentExpression(map, group, rule.Dependent, ruleIndex, result);
        }
    }

    private void ValidateSource(SourceExpression source, string location, ValidationResult result)
    {
        // Validate context exists
        if (source.Context == null)
        {
            result.AddError("Source context is required", location: location, code: "NO_SOURCE_CONTEXT");
        }

        // Check for conflicting conditions
        if (source.Condition != null && source.Default != null)
        {
            result.AddWarning("Source has both 'where' condition and 'default' value - default will only be used if source is initially empty",
                location: location, code: "WHERE_WITH_DEFAULT");
        }

        // Validate check/log require FHIRPath
        if (source.Check != null || source.Log != null)
        {
            // Note: In actual execution, FHIRPath evaluator is required
            // This is just a warning since it's a runtime dependency
        }
    }

    private void ValidateTarget(TargetExpression target, string location, ValidationResult result)
    {
        // Validate transform if present
        if (target.Transform is TransformExpression transform)
        {
            ValidateTransform(transform, location, result);
        }

        // Validate list mode combinations
        if (target.ListMode.HasValue)
        {
            var listMode = target.ListMode.Value;

            // Only_one should probably have a check clause
            if (listMode == ListMode.OnlyOne)
            {
                // This is just informational
            }
        }
    }

    private void ValidateDependentExpression(MapExpression map, GroupExpression group, Expression dependent, int ruleIndex, ValidationResult result)
    {
        switch (dependent)
        {
            case RuleSetExpression ruleSet:
                // Validate nested rules
                foreach (var nestedRule in ruleSet.Rules)
                {
                    ValidateRule(map, group, nestedRule, ruleIndex, result);
                }
                break;

            case GroupInvocationExpression groupInvocation:
                // Validate group invocation exists
                ValidateGroupInvocation(map, groupInvocation, result);
                break;

            default:
                result.AddError($"Unknown dependent expression type: {dependent.GetType().Name}",
                    location: $"Group: {group.Name}, Rule {ruleIndex}",
                    code: "UNKNOWN_DEPENDENT_TYPE");
                break;
        }
    }

    private void ValidateGroupInvocation(MapExpression map, GroupInvocationExpression invocation, ValidationResult result)
    {
        // Check if the group exists in the current map
        var groupExists = map.Groups.Any(g =>
            string.Equals(g.Name, invocation.GroupName, StringComparison.OrdinalIgnoreCase));

        if (!groupExists && _importResolver != null)
        {
            // Check if it exists in imports
            var importedGroup = _importResolver.FindGroup(map, invocation.GroupName);
            groupExists = importedGroup != null;
        }

        if (!groupExists)
        {
            result.AddError($"Group '{invocation.GroupName}' not found",
                location: "Group Invocation",
                code: "UNKNOWN_GROUP");
        }
    }

    private void ValidateTransform(TransformExpression transform, string location, ValidationResult result)
    {
        // Check if transform function exists
        var standardTransform = Transforms.StandardTransforms.Get(transform.FunctionName);
        if (standardTransform == null)
        {
            result.AddWarning($"Unknown transform function: {transform.FunctionName}",
                location: location, code: "UNKNOWN_TRANSFORM");
        }

        // Validate argument count (basic check)
        if (transform.FunctionName == "create" && transform.Arguments.Count == 0)
        {
            result.AddError("create() requires a type argument", location: location, code: "MISSING_ARGUMENT");
        }

        if (transform.FunctionName == "translate" && transform.Arguments.Count < 3)
        {
            result.AddError("translate() requires source, map_uri, and output arguments",
                location: location, code: "MISSING_ARGUMENT");
        }

        if (transform.FunctionName == "cast" && transform.Arguments.Count < 2)
        {
            result.AddError("cast() requires source and type arguments",
                location: location, code: "MISSING_ARGUMENT");
        }
    }

    private void ValidateImports(MapExpression map, ValidationResult result)
    {
        foreach (var import in map.Imports)
        {
            if (string.IsNullOrWhiteSpace(import.Url))
            {
                result.AddError("Import URL is required", location: "Imports", code: "MISSING_IMPORT_URL");
            }

            // If import resolver available, check if import can be loaded
            if (_importResolver != null && !string.IsNullOrWhiteSpace(import.Url))
            {
                var importedMap = _importResolver.GetImportedMap(import.Url);
                if (importedMap == null)
                {
                    result.AddWarning($"Import '{import.Url}' could not be resolved",
                        location: "Imports", code: "UNRESOLVED_IMPORT");
                }
            }
        }
    }
}
