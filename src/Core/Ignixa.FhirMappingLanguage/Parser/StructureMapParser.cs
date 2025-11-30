/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Parses a FHIR StructureMap resource into a MapExpression AST.
 * Enables conversion: StructureMap Resource → AST.
 */

using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Parses a FHIR StructureMap resource into a MapExpression AST.
/// Enables conversion: StructureMap Resource → AST.
/// </summary>
public class StructureMapParser
{
    /// <summary>
    /// Parses a FHIR StructureMap resource into a MapExpression AST.
    /// </summary>
    /// <param name="structureMap">The StructureMap resource as StructureMapJsonNode</param>
    /// <returns>The parsed MapExpression</returns>
    /// <exception cref="ArgumentNullException">Thrown when structureMap is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when required fields are missing</exception>
    public MapExpression Parse(StructureMapJsonNode structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);

        // Required fields
        var url = structureMap.Url ?? throw new InvalidOperationException("StructureMap.url is required");
        var name = structureMap.Name ?? throw new InvalidOperationException("StructureMap.name is required");

        // Parse optional collections
        var uses = ParseStructures(structureMap.Structure);
        var imports = ParseImports(structureMap.Import);
        var groups = ParseGroups(structureMap.Group);
        var conceptMaps = ParseContainedConceptMaps(structureMap.Contained);

        return new MapExpression(url, name, uses, imports, groups, conceptMaps, []);
    }

    /// <summary>
    /// Parses a FHIR StructureMap resource from JsonNode (backward compatibility).
    /// </summary>
    /// <param name="structureMap">The StructureMap resource as JsonNode</param>
    /// <returns>The parsed MapExpression</returns>
    public MapExpression Parse(JsonNode structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);

        // Validate resourceType
        var obj = structureMap.AsObject();
        var resourceType = obj["resourceType"]?.GetValue<string>();
        if (resourceType != "StructureMap")
        {
            throw new InvalidOperationException($"Expected resourceType 'StructureMap', got '{resourceType}'");
        }

        // Convert to typed model and delegate
        var typed = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(structureMap.ToJsonString());
        return Parse(typed);
    }

    /// <summary>
    /// Convenience overload for JsonObject.
    /// </summary>
    public MapExpression Parse(JsonObject structureMap) => Parse((JsonNode)structureMap);

    /// <summary>
    /// Parses structure[] array into UsesExpression[].
    /// </summary>
    private static List<UsesExpression> ParseStructures(IEnumerable<StructureMapStructureJsonNode>? structures)
    {
        if (structures is null)
        {
            return [];
        }

        return structures
            .Where(s => s.Url is not null)
            .Select(s => new UsesExpression(
                s.Url!,
                s.Alias,
                ConvertModelMode(s.Mode)))
            .ToList();
    }

    /// <summary>
    /// Parses import[] array into ImportsExpression[].
    /// </summary>
    private static List<ImportsExpression> ParseImports(IEnumerable<string>? imports)
    {
        if (imports is null)
        {
            return [];
        }

        return imports.Select(url => new ImportsExpression(url)).ToList();
    }

    /// <summary>
    /// Parses group[] array into GroupExpression[].
    /// </summary>
    private static List<GroupExpression> ParseGroups(IEnumerable<StructureMapGroupJsonNode>? groups)
    {
        if (groups is null)
        {
            return [];
        }

        return groups
            .Where(g => g.Name is not null)
            .Select(g => new GroupExpression(
                g.Name!,
                ParseInputParameters(g.Input),
                g.Extends,
                ParseRules(g.Rule)))
            .ToList();
    }

    /// <summary>
    /// Parses input[] array into ParameterExpression[].
    /// </summary>
    private static List<ParameterExpression> ParseInputParameters(IEnumerable<StructureMapInputJsonNode>? inputs)
    {
        if (inputs is null)
        {
            return [];
        }

        return inputs
            .Where(i => i.Name is not null)
            .Select(i => new ParameterExpression(
                ConvertParameterMode(i.Mode),
                i.Name!,
                i.Type))
            .ToList();
    }

    /// <summary>
    /// Parses rule[] array into RuleExpression[].
    /// </summary>
    private static List<RuleExpression> ParseRules(IEnumerable<StructureMapRuleJsonNode>? rules)
    {
        if (rules is null)
        {
            return [];
        }

        return rules.Select(r =>
        {
            var sources = ParseSources(r.Source);
            var targets = ParseTargets(r.Target);
            var dependent = ParseDependent(r.Rule, r.Dependent);

            return new RuleExpression(r.Name, sources, targets, dependent);
        }).ToList();
    }

    /// <summary>
    /// Parses source[] array into SourceExpression[].
    /// </summary>
    private static List<SourceExpression> ParseSources(IEnumerable<StructureMapSourceJsonNode>? sources)
    {
        if (sources is null)
        {
            return [];
        }

        return sources
            .Where(s => s.Context is not null)
            .Select(s =>
            {
                // Build context expression (context + optional element)
                Expression contextExpr = new IdentifierExpression(s.Context!);
                if (s.Element is not null)
                {
                    contextExpr = new QualifiedIdentifierExpression(contextExpr, s.Element);
                }

                // Parse optional expressions
                Expression? condition = ParseFhirPathString(s.Condition);
                Expression? check = ParseFhirPathString(s.Check);
                Expression? log = ParseStringExpression(s.LogMessage);
                Expression? defaultValue = ParseDefaultValue(s);

                // Parse cardinality
                Cardinality? cardinality = ParseCardinality(s.Min, s.Max);

                return new SourceExpression(
                    contextExpr,
                    s.Variable,
                    s.Type,
                    condition,
                    check,
                    log,
                    defaultValue,
                    cardinality);
            })
            .ToList();
    }

    /// <summary>
    /// Parses target[] array into TargetExpression[].
    /// </summary>
    private static List<TargetExpression> ParseTargets(IEnumerable<StructureMapTargetJsonNode>? targets)
    {
        if (targets is null)
        {
            return [];
        }

        return targets.Select(t =>
        {
            // Parse context and element
            Expression? contextExpr = null;
            if (t.Context is not null)
            {
                contextExpr = new IdentifierExpression(t.Context);
                if (t.Element is not null)
                {
                    contextExpr = new QualifiedIdentifierExpression(contextExpr, t.Element);
                }
            }

            // Parse transform
            Expression? transform = ParseTransform(t);

            // Parse list mode - take first element if available
            ListMode? listMode = null;
            var listModeValue = t.ListMode.FirstOrDefault();
            if (listModeValue is not null)
            {
                listMode = ParseListMode(listModeValue);
            }

            return new TargetExpression(contextExpr, t.Variable, transform, listMode);
        }).ToList();
    }

    /// <summary>
    /// Parses transform and parameter[] into TransformExpression.
    /// </summary>
    private static Expression? ParseTransform(StructureMapTargetJsonNode target)
    {
        if (target.Transform is null)
        {
            return null;
        }

        var transformName = target.Transform.Value.GetLiteral();
        var parameters = ParseTransformParameters(target.Parameter);
        return new TransformExpression(transformName, parameters);
    }

    /// <summary>
    /// Parses parameter[] array into Expression[] for transforms.
    /// </summary>
    private static List<Expression> ParseTransformParameters(IEnumerable<StructureMapParameterJsonNode>? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        List<Expression> result = [];

        foreach (var param in parameters)
        {
            var valueNode = param.GetValue();
            if (valueNode is null) continue;

            // Determine type from property name
            foreach (var prop in param.MutableNode)
            {
                if (!prop.Key.StartsWith("value", StringComparison.Ordinal)) continue;

                var suffix = prop.Key.Substring(5); // Remove "value" prefix

                switch (suffix.ToLowerInvariant())
                {
                    case "string":
                        result.Add(new LiteralExpression(param.GetValueAs<string>() ?? string.Empty));
                        break;
                    case "integer":
                        result.Add(new LiteralExpression(param.GetValueAs<int>()));
                        break;
                    case "decimal":
                        result.Add(new LiteralExpression(param.GetValueAs<decimal>()));
                        break;
                    case "boolean":
                        result.Add(new LiteralExpression(param.GetValueAs<bool>()));
                        break;
                    case "id":
                        var idValue = param.GetValueAs<string>();
                        if (idValue is not null)
                        {
                            result.Add(new IdentifierExpression(idValue));
                        }
                        break;
                }
                break; // Only process first value[x] property
            }
        }

        return result;
    }

    /// <summary>
    /// Parses dependent clause (nested rules or group invocations).
    /// </summary>
    private static Expression? ParseDependent(
        IEnumerable<StructureMapRuleJsonNode>? nestedRules,
        IEnumerable<StructureMapDependentJsonNode>? dependentCalls)
    {
        // Check for nested rules first (RuleSetExpression)
        if (nestedRules is not null && nestedRules.Any())
        {
            var rules = ParseRules(nestedRules);
            return new RuleSetExpression(rules);
        }

        // Check for dependent group invocations
        if (dependentCalls is not null)
        {
            var firstDependent = dependentCalls.FirstOrDefault();
            if (firstDependent?.Name is not null)
            {
                var parameters = ParseInvocationParameters(firstDependent.Parameter);
                return new GroupInvocationExpression(firstDependent.Name, parameters);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses parameter[] for group invocations.
    /// </summary>
    private static List<Expression> ParseInvocationParameters(IEnumerable<StructureMapParameterJsonNode>? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        List<Expression> result = [];

        foreach (var param in parameters)
        {
            var valueNode = param.GetValue();
            if (valueNode is null) continue;

            // Check property names to determine type
            foreach (var prop in param.MutableNode)
            {
                if (!prop.Key.StartsWith("value", StringComparison.Ordinal)) continue;

                var suffix = prop.Key.Substring(5);

                switch (suffix.ToLowerInvariant())
                {
                    case "string":
                        var strValue = param.GetValueAs<string>();
                        if (strValue is not null)
                        {
                            result.Add(new LiteralExpression(strValue));
                        }
                        break;
                    case "id":
                        var idValue = param.GetValueAs<string>();
                        if (idValue is not null)
                        {
                            result.Add(new IdentifierExpression(idValue));
                        }
                        break;
                }
                break; // Only process first value[x] property
            }
        }

        return result;
    }

    /// <summary>
    /// Parses min/max into Cardinality.
    /// </summary>
    private static Cardinality? ParseCardinality(int? min, string? max)
    {
        if (min is null && max is null)
        {
            return null;
        }

        var minValue = min ?? 0;

        int? maxValue = null;
        if (max is not null && max != "*" && int.TryParse(max, out var parsedMax))
        {
            maxValue = parsedMax;
        }

        return new Cardinality(minValue, maxValue);
    }

    /// <summary>
    /// Parses default value[x] into Expression.
    /// </summary>
    private static Expression? ParseDefaultValue(StructureMapSourceJsonNode source)
    {
        var defaultNode = source.GetDefaultValue();
        if (defaultNode is null)
        {
            return null;
        }

        // Inspect MutableNode to find the actual property name
        foreach (var prop in source.MutableNode)
        {
            if (!prop.Key.StartsWith("default", StringComparison.Ordinal)) continue;

            var suffix = prop.Key.Substring(7); // Remove "default" prefix

            switch (suffix.ToLowerInvariant())
            {
                case "string":
                    var strValue = defaultNode.GetValue<string>();
                    return new LiteralExpression(strValue);
                case "integer":
                    var intValue = defaultNode.GetValue<int>();
                    return new LiteralExpression(intValue);
                case "boolean":
                    var boolValue = defaultNode.GetValue<bool>();
                    return new LiteralExpression(boolValue);
            }
            break; // Only process first default[x] property
        }

        return null;
    }

    /// <summary>
    /// Parses a FHIRPath expression string into a FhirPathExpression.
    /// </summary>
    private static Expression? ParseFhirPathString(string? expression) =>
        string.IsNullOrWhiteSpace(expression) ? null : new FhirPathExpression(expression);

    /// <summary>
    /// Parses a string into a LiteralExpression.
    /// </summary>
    private static Expression? ParseStringExpression(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new LiteralExpression(value);

    /// <summary>
    /// Converts StructureMapModelMode to ModelMode.
    /// </summary>
    private static ModelMode ConvertModelMode(StructureMapModelMode? mode) => mode switch
    {
        StructureMapModelMode.Source => ModelMode.Source,
        StructureMapModelMode.Queried => ModelMode.Queried,
        StructureMapModelMode.Target => ModelMode.Target,
        StructureMapModelMode.Produced => ModelMode.Produced,
        _ => ModelMode.Source
    };

    /// <summary>
    /// Converts StructureMapInputMode to ParameterMode.
    /// </summary>
    private static ParameterMode ConvertParameterMode(StructureMapInputMode? mode) => mode switch
    {
        StructureMapInputMode.Source => ParameterMode.Source,
        StructureMapInputMode.Target => ParameterMode.Target,
        _ => ParameterMode.Source
    };

    /// <summary>
    /// Parses ListMode from string.
    /// </summary>
    private static ListMode ParseListMode(string mode) => mode.ToLowerInvariant() switch
    {
        "first" => ListMode.First,
        "notfirst" or "not_first" => ListMode.NotFirst,
        "last" => ListMode.Last,
        "notlast" or "not_last" => ListMode.NotLast,
        "onlyone" or "only_one" => ListMode.OnlyOne,
        "share" => ListMode.Share,
        "single" => ListMode.Single,
        _ => ListMode.First
    };

    /// <summary>
    /// Parses contained[] array for ConceptMap resources.
    /// </summary>
    private static List<ConceptMapDeclarationExpression> ParseContainedConceptMaps(
        IEnumerable<ResourceJsonNode>? contained)
    {
        if (contained is null)
        {
            return [];
        }

        return contained
            .Where(r => r.ResourceType == "ConceptMap")
            .Select(ParseConceptMap)
            .Where(cm => cm is not null)
            .ToList()!;
    }

    /// <summary>
    /// Parses a single ConceptMap resource into a ConceptMapDeclarationExpression.
    /// </summary>
    private static ConceptMapDeclarationExpression? ParseConceptMap(ResourceJsonNode conceptMap)
    {
        var id = conceptMap.Id;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        // Build identifier with # prefix for inline reference
        var identifier = $"#{id}";

        // Parse groups from the raw JsonNode
        List<ConceptMapPrefixExpression> prefixes = [];
        List<ConceptMapGroupExpression> groups = [];

        var groupArray = conceptMap.MutableNode["group"]?.AsArray();
        if (groupArray is null)
        {
            return new ConceptMapDeclarationExpression(identifier, prefixes, groups);
        }

        foreach (var groupItem in groupArray)
        {
            if (groupItem is null) continue;

            var groupObj = groupItem.AsObject();
            var sourceUrl = groupObj["source"]?.GetValue<string>();
            var targetUrl = groupObj["target"]?.GetValue<string>();

            // Create prefix entries
            var sourcePrefix = "s";
            var targetPrefix = "t";

            if (sourceUrl is not null && !prefixes.Any(p => p.Url == sourceUrl))
            {
                prefixes.Add(new ConceptMapPrefixExpression(sourcePrefix, sourceUrl));
            }
            if (targetUrl is not null && !prefixes.Any(p => p.Url == targetUrl))
            {
                prefixes.Add(new ConceptMapPrefixExpression(targetPrefix, targetUrl));
            }

            // Parse element mappings
            List<ConceptMapCodeMapExpression> codeMaps = [];
            var elementArray = groupObj["element"]?.AsArray();
            if (elementArray is not null)
            {
                foreach (var elementItem in elementArray)
                {
                    if (elementItem is null) continue;

                    var elementObj = elementItem.AsObject();
                    var sourceCode = elementObj["code"]?.GetValue<string>();
                    if (sourceCode is null) continue;

                    var targetArray = elementObj["target"]?.AsArray();
                    if (targetArray is not null)
                    {
                        foreach (var targetItem in targetArray)
                        {
                            if (targetItem is null) continue;

                            var targetObj = targetItem.AsObject();
                            var targetCode = targetObj["code"]?.GetValue<string>();
                            var equivalenceStr = targetObj["equivalence"]?.GetValue<string>() ?? "equivalent";

                            if (targetCode is not null)
                            {
                                var equivalence = ParseEquivalence(equivalenceStr);
                                codeMaps.Add(new ConceptMapCodeMapExpression(
                                    sourcePrefix,
                                    sourceCode,
                                    equivalence,
                                    targetPrefix,
                                    targetCode));
                            }
                        }
                    }
                }
            }

            groups.Add(new ConceptMapGroupExpression(sourceUrl, targetUrl, codeMaps));
        }

        return new ConceptMapDeclarationExpression(identifier, prefixes, groups);
    }

    /// <summary>
    /// Parses ConceptMapEquivalence from string.
    /// </summary>
    private static ConceptMapEquivalence ParseEquivalence(string equivalence) => equivalence.ToLowerInvariant() switch
    {
        "equivalent" => ConceptMapEquivalence.Equivalent,
        "relatedto" => ConceptMapEquivalence.RelatedTo,
        "wider" => ConceptMapEquivalence.Broader,
        "narrower" => ConceptMapEquivalence.Narrower,
        "unmatched" => ConceptMapEquivalence.NotRelatedTo,
        _ => ConceptMapEquivalence.Equivalent
    };
}
