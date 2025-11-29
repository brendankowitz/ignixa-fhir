/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Parses a FHIR StructureMap resource (JsonNode) into a MapExpression AST.
 * Enables conversion: StructureMap Resource → AST.
 */

using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Parses a FHIR StructureMap resource (JsonNode) into a MapExpression AST.
/// Enables conversion: StructureMap Resource → AST.
/// </summary>
public class StructureMapParser
{
    /// <summary>
    /// Parses a FHIR StructureMap resource into a MapExpression AST.
    /// </summary>
    /// <param name="structureMap">The StructureMap resource as JsonNode</param>
    /// <returns>The parsed MapExpression</returns>
    /// <exception cref="ArgumentNullException">Thrown when structureMap is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when required fields are missing</exception>
    public MapExpression Parse(JsonNode structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);

        var obj = structureMap.AsObject();

        // Validate resourceType
        var resourceType = obj["resourceType"]?.GetValue<string>();
        if (resourceType != "StructureMap")
        {
            throw new InvalidOperationException($"Expected resourceType 'StructureMap', got '{resourceType}'");
        }

        // Required fields
        var url = obj["url"]?.GetValue<string>()
            ?? throw new InvalidOperationException("StructureMap.url is required");
        var name = obj["name"]?.GetValue<string>()
            ?? throw new InvalidOperationException("StructureMap.name is required");

        // Parse optional collections
        var uses = ParseStructures(obj["structure"]?.AsArray());
        var imports = ParseImports(obj["import"]?.AsArray());
        var groups = ParseGroups(obj["group"]?.AsArray());
        var conceptMaps = ParseContainedConceptMaps(obj["contained"]?.AsArray());

        return new MapExpression(url, name, uses, imports, groups, conceptMaps, []);
    }

    /// <summary>
    /// Convenience overload for JsonObject.
    /// </summary>
    public MapExpression Parse(JsonObject structureMap) => Parse((JsonNode)structureMap);

    /// <summary>
    /// Parses structure[] array into UsesExpression[].
    /// </summary>
    private static List<UsesExpression> ParseStructures(JsonArray? structures)
    {
        if (structures is null)
        {
            return [];
        }

        var result = new List<UsesExpression>();

        foreach (var item in structures)
        {
            if (item is null) continue;

            var obj = item.AsObject();
            var url = obj["url"]?.GetValue<string>();
            if (url is null) continue;

            var alias = obj["alias"]?.GetValue<string>();
            var modeString = obj["mode"]?.GetValue<string>() ?? "source";
            var mode = ParseModelMode(modeString);

            result.Add(new UsesExpression(url, alias, mode));
        }

        return result;
    }

    /// <summary>
    /// Parses import[] array into ImportsExpression[].
    /// </summary>
    private static List<ImportsExpression> ParseImports(JsonArray? imports)
    {
        if (imports is null)
        {
            return [];
        }

        var result = new List<ImportsExpression>();

        foreach (var item in imports)
        {
            if (item is null) continue;

            var url = item.GetValue<string>();
            result.Add(new ImportsExpression(url));
        }

        return result;
    }

    /// <summary>
    /// Parses group[] array into GroupExpression[].
    /// </summary>
    private static List<GroupExpression> ParseGroups(JsonArray? groups)
    {
        if (groups is null)
        {
            return [];
        }

        var result = new List<GroupExpression>();

        foreach (var item in groups)
        {
            if (item is null) continue;

            var obj = item.AsObject();
            var name = obj["name"]?.GetValue<string>();
            if (name is null) continue;

            var extends_ = obj["extends"]?.GetValue<string>();
            var parameters = ParseInputParameters(obj["input"]?.AsArray());
            var rules = ParseRules(obj["rule"]?.AsArray());

            result.Add(new GroupExpression(name, parameters, extends_, rules));
        }

        return result;
    }

    /// <summary>
    /// Parses input[] array into ParameterExpression[].
    /// </summary>
    private static List<ParameterExpression> ParseInputParameters(JsonArray? inputs)
    {
        if (inputs is null)
        {
            return [];
        }

        var result = new List<ParameterExpression>();

        foreach (var item in inputs)
        {
            if (item is null) continue;

            var obj = item.AsObject();
            var name = obj["name"]?.GetValue<string>();
            if (name is null) continue;

            var type = obj["type"]?.GetValue<string>();
            var modeString = obj["mode"]?.GetValue<string>() ?? "source";
            var mode = ParseParameterMode(modeString);

            result.Add(new ParameterExpression(mode, name, type));
        }

        return result;
    }

    /// <summary>
    /// Parses rule[] array into RuleExpression[].
    /// </summary>
    private static List<RuleExpression> ParseRules(JsonArray? rules)
    {
        if (rules is null)
        {
            return [];
        }

        var result = new List<RuleExpression>();

        foreach (var item in rules)
        {
            if (item is null) continue;

            var obj = item.AsObject();
            var name = obj["name"]?.GetValue<string>();
            var sources = ParseSources(obj["source"]?.AsArray());
            var targets = ParseTargets(obj["target"]?.AsArray());
            var dependent = ParseDependent(obj["rule"]?.AsArray(), obj["dependent"]?.AsArray());

            result.Add(new RuleExpression(name, sources, targets, dependent));
        }

        return result;
    }

    /// <summary>
    /// Parses source[] array into SourceExpression[].
    /// </summary>
    private static List<SourceExpression> ParseSources(JsonArray? sources)
    {
        if (sources is null)
        {
            return [];
        }

        var result = new List<SourceExpression>();

        foreach (var item in sources)
        {
            if (item is null) continue;

            var obj = item.AsObject();

            // Parse context (required) and optional element to build qualified identifier
            var context = obj["context"]?.GetValue<string>();
            if (context is null) continue;

            Expression contextExpr = new IdentifierExpression(context);
            var element = obj["element"]?.GetValue<string>();
            if (element is not null)
            {
                contextExpr = new QualifiedIdentifierExpression(contextExpr, element);
            }

            var variable = obj["variable"]?.GetValue<string>();
            var type = obj["type"]?.GetValue<string>();

            // Parse optional expressions
            Expression? condition = ParseFhirPathString(obj["condition"]?.GetValue<string>());
            Expression? check = ParseFhirPathString(obj["check"]?.GetValue<string>());
            Expression? log = ParseStringExpression(obj["logMessage"]?.GetValue<string>());
            Expression? defaultValue = ParseDefaultValue(obj);

            // Parse cardinality
            Cardinality? cardinality = ParseCardinality(obj);

            result.Add(new SourceExpression(
                contextExpr,
                variable,
                type,
                condition,
                check,
                log,
                defaultValue,
                cardinality));
        }

        return result;
    }

    /// <summary>
    /// Parses target[] array into TargetExpression[].
    /// </summary>
    private static List<TargetExpression> ParseTargets(JsonArray? targets)
    {
        if (targets is null)
        {
            return [];
        }

        var result = new List<TargetExpression>();

        foreach (var item in targets)
        {
            if (item is null) continue;

            var obj = item.AsObject();

            // Parse context and element
            Expression? contextExpr = null;
            var context = obj["context"]?.GetValue<string>();
            if (context is not null)
            {
                contextExpr = new IdentifierExpression(context);
                var element = obj["element"]?.GetValue<string>();
                if (element is not null)
                {
                    contextExpr = new QualifiedIdentifierExpression(contextExpr, element);
                }
            }

            var variable = obj["variable"]?.GetValue<string>();

            // Parse transform
            Expression? transform = ParseTransform(obj);

            // Parse list mode - can be a string or an array with one element
            ListMode? listMode = null;
            var listModeNode = obj["listMode"];
            if (listModeNode is not null)
            {
                string? listModeString = listModeNode switch
                {
                    JsonValue v => v.GetValue<string>(),
                    JsonArray arr when arr.Count > 0 => arr[0]?.GetValue<string>(),
                    _ => null
                };

                if (listModeString is not null)
                {
                    listMode = ParseListMode(listModeString);
                }
            }

            result.Add(new TargetExpression(contextExpr, variable, transform, listMode));
        }

        return result;
    }

    /// <summary>
    /// Parses transform and parameter[] into TransformExpression.
    /// </summary>
    private static Expression? ParseTransform(JsonObject target)
    {
        var transformName = target["transform"]?.GetValue<string>();
        if (transformName is null)
        {
            return null;
        }

        var parameters = ParseTransformParameters(target["parameter"]?.AsArray());
        return new TransformExpression(transformName, parameters);
    }

    /// <summary>
    /// Parses parameter[] array into Expression[] for transforms.
    /// </summary>
    private static List<Expression> ParseTransformParameters(JsonArray? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        var result = new List<Expression>();

        foreach (var item in parameters)
        {
            if (item is null) continue;

            var obj = item.AsObject();

            // Try different value[x] properties
            if (obj["valueString"] is JsonNode strNode)
            {
                result.Add(new LiteralExpression(strNode.GetValue<string>()));
            }
            else if (obj["valueInteger"] is JsonNode intNode)
            {
                result.Add(new LiteralExpression(intNode.GetValue<int>()));
            }
            else if (obj["valueDecimal"] is JsonNode decNode)
            {
                result.Add(new LiteralExpression(decNode.GetValue<decimal>()));
            }
            else if (obj["valueBoolean"] is JsonNode boolNode)
            {
                result.Add(new LiteralExpression(boolNode.GetValue<bool>()));
            }
            else if (obj["valueId"] is JsonNode idNode)
            {
                result.Add(new IdentifierExpression(idNode.GetValue<string>()));
            }
        }

        return result;
    }

    /// <summary>
    /// Parses dependent clause (nested rules or group invocations).
    /// </summary>
    private static Expression? ParseDependent(JsonArray? nestedRules, JsonArray? dependentCalls)
    {
        // Check for nested rules first (RuleSetExpression)
        if (nestedRules is not null && nestedRules.Count > 0)
        {
            var rules = ParseRules(nestedRules);
            return new RuleSetExpression(rules);
        }

        // Check for dependent group invocations
        if (dependentCalls is not null && dependentCalls.Count > 0)
        {
            // For simplicity, return the first dependent call
            // In a full implementation, this might need to handle multiple calls
            var first = dependentCalls[0];
            if (first is not null)
            {
                var obj = first.AsObject();
                var name = obj["name"]?.GetValue<string>();
                if (name is not null)
                {
                    var parameters = ParseInvocationParameters(obj["parameter"]?.AsArray());
                    return new GroupInvocationExpression(name, parameters);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses parameter[] for group invocations.
    /// </summary>
    private static List<Expression> ParseInvocationParameters(JsonArray? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        var result = new List<Expression>();

        foreach (var item in parameters)
        {
            if (item is null) continue;

            var obj = item.AsObject();

            // Try different value[x] properties
            if (obj["valueString"] is JsonNode strNode)
            {
                result.Add(new LiteralExpression(strNode.GetValue<string>()));
            }
            else if (obj["valueId"] is JsonNode idNode)
            {
                result.Add(new IdentifierExpression(idNode.GetValue<string>()));
            }
        }

        return result;
    }

    /// <summary>
    /// Parses min/max into Cardinality.
    /// </summary>
    private static Cardinality? ParseCardinality(JsonObject source)
    {
        var minNode = source["min"];
        var maxNode = source["max"];

        if (minNode is null && maxNode is null)
        {
            return null;
        }

        var min = minNode?.GetValue<int>() ?? 0;

        int? max = null;
        if (maxNode is not null)
        {
            var maxString = maxNode.GetValue<string>();
            if (maxString != "*" && int.TryParse(maxString, out var maxValue))
            {
                max = maxValue;
            }
            // If maxString is "*", max remains null (unbounded)
        }

        return new Cardinality(min, max);
    }

    /// <summary>
    /// Parses default value[x] into Expression.
    /// </summary>
    private static Expression? ParseDefaultValue(JsonObject source)
    {
        if (source["defaultValueString"] is JsonNode strNode)
        {
            return new LiteralExpression(strNode.GetValue<string>());
        }
        if (source["defaultValueInteger"] is JsonNode intNode)
        {
            return new LiteralExpression(intNode.GetValue<int>());
        }
        if (source["defaultValueBoolean"] is JsonNode boolNode)
        {
            return new LiteralExpression(boolNode.GetValue<bool>());
        }

        return null;
    }

    /// <summary>
    /// Parses a FHIRPath expression string into a FhirPathExpression.
    /// </summary>
    private static Expression? ParseFhirPathString(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        return new FhirPathExpression(expression);
    }

    /// <summary>
    /// Parses a string into a LiteralExpression.
    /// </summary>
    private static Expression? ParseStringExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new LiteralExpression(value);
    }

    /// <summary>
    /// Parses ModelMode from string.
    /// </summary>
    private static ModelMode ParseModelMode(string mode) => mode.ToLowerInvariant() switch
    {
        "source" => ModelMode.Source,
        "queried" => ModelMode.Queried,
        "target" => ModelMode.Target,
        "produced" => ModelMode.Produced,
        _ => ModelMode.Source // Default fallback
    };

    /// <summary>
    /// Parses ParameterMode from string.
    /// </summary>
    private static ParameterMode ParseParameterMode(string mode) => mode.ToLowerInvariant() switch
    {
        "source" => ParameterMode.Source,
        "target" => ParameterMode.Target,
        _ => ParameterMode.Source // Default fallback
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
        _ => ListMode.First // Default fallback
    };

    /// <summary>
    /// Parses contained[] array for ConceptMap resources.
    /// </summary>
    private static List<ConceptMapDeclarationExpression> ParseContainedConceptMaps(JsonArray? contained)
    {
        if (contained is null)
        {
            return [];
        }

        var result = new List<ConceptMapDeclarationExpression>();

        foreach (var item in contained)
        {
            if (item is null) continue;

            var obj = item.AsObject();
            var resourceType = obj["resourceType"]?.GetValue<string>();

            // Only process ConceptMap resources
            if (resourceType != "ConceptMap") continue;

            var id = obj["id"]?.GetValue<string>();
            if (id is null) continue;

            // Build identifier with # prefix for inline reference
            var identifier = $"#{id}";

            // Parse groups into prefixes and code mappings
            var prefixes = new List<ConceptMapPrefixExpression>();
            var groups = new List<ConceptMapGroupExpression>();

            var groupArray = obj["group"]?.AsArray();
            if (groupArray is not null)
            {
                foreach (var groupItem in groupArray)
                {
                    if (groupItem is null) continue;

                    var groupObj = groupItem.AsObject();
                    var sourceUrl = groupObj["source"]?.GetValue<string>();
                    var targetUrl = groupObj["target"]?.GetValue<string>();

                    // Create prefix entries from source/target URLs
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
                    var codeMaps = new List<ConceptMapCodeMapExpression>();
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
            }

            result.Add(new ConceptMapDeclarationExpression(identifier, prefixes, groups));
        }

        return result;
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
