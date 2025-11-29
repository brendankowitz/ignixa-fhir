/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Builds a FHIR StructureMap resource from a MapExpression AST.
 */

using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Serialization;

/// <summary>
/// Builds a FHIR StructureMap resource (as JsonNode) from a MapExpression AST.
/// Enables conversion: AST → StructureMap Resource.
/// </summary>
public class StructureMapBuilder
{
    /// <summary>
    /// Builds a FHIR StructureMap resource from a MapExpression AST.
    /// </summary>
    /// <param name="map">The parsed map expression.</param>
    /// <returns>A JsonObject representing the StructureMap resource.</returns>
    public JsonObject Build(MapExpression map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var structureMap = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = map.Url,
            ["name"] = map.Identifier,
            ["status"] = "active"
        };

        // Add structure array (uses declarations)
        if (map.Uses.Count > 0)
        {
            var structureArray = new JsonArray();
            foreach (var uses in map.Uses)
            {
                structureArray.Add(BuildStructure(uses));
            }
            structureMap["structure"] = structureArray;
        }

        // Add import array
        if (map.Imports.Count > 0)
        {
            var importArray = new JsonArray();
            foreach (var import in map.Imports)
            {
                importArray.Add(import.Url);
            }
            structureMap["import"] = importArray;
        }

        // Add group array
        if (map.Groups.Count > 0)
        {
            var groupArray = new JsonArray();
            foreach (var group in map.Groups)
            {
                groupArray.Add(BuildGroup(group));
            }
            structureMap["group"] = groupArray;
        }

        return structureMap;
    }

    /// <summary>
    /// Builds a structure element from a UsesExpression.
    /// </summary>
    private static JsonObject BuildStructure(UsesExpression uses)
    {
        var structure = new JsonObject
        {
            ["url"] = uses.Url,
            ["mode"] = ModelModeToString(uses.Mode)
        };

        if (uses.Alias is not null)
        {
            structure["alias"] = uses.Alias;
        }

        return structure;
    }

    /// <summary>
    /// Builds a group element from a GroupExpression.
    /// </summary>
    private static JsonObject BuildGroup(GroupExpression group)
    {
        var groupObj = new JsonObject
        {
            ["name"] = group.Name,
            ["typeMode"] = "none"  // Default type mode per FHIR spec
        };

        // Add extends if present
        if (group.Extends is not null)
        {
            groupObj["extends"] = group.Extends;
        }

        // Add input parameters
        if (group.Parameters.Count > 0)
        {
            var inputArray = new JsonArray();
            foreach (var param in group.Parameters)
            {
                inputArray.Add(BuildInput(param));
            }
            groupObj["input"] = inputArray;
        }

        // Add rules
        if (group.Rules.Count > 0)
        {
            var ruleArray = new JsonArray();
            foreach (var rule in group.Rules)
            {
                ruleArray.Add(BuildRule(rule));
            }
            groupObj["rule"] = ruleArray;
        }

        return groupObj;
    }

    /// <summary>
    /// Builds an input element from a ParameterExpression.
    /// </summary>
    private static JsonObject BuildInput(ParameterExpression parameter)
    {
        var input = new JsonObject
        {
            ["name"] = parameter.Name,
            ["mode"] = ParameterModeToString(parameter.Mode)
        };

        if (parameter.Type is not null)
        {
            input["type"] = parameter.Type;
        }

        return input;
    }

    /// <summary>
    /// Builds a rule element from a RuleExpression.
    /// </summary>
    private static JsonObject BuildRule(RuleExpression rule)
    {
        var ruleObj = new JsonObject();

        // Only add name if one was explicitly provided (preserve null for unnamed rules)
        if (rule.Name is not null)
        {
            ruleObj["name"] = rule.Name;
        }

        // Add sources
        if (rule.Sources.Count > 0)
        {
            var sourceArray = new JsonArray();
            foreach (var source in rule.Sources)
            {
                sourceArray.Add(BuildSource(source));
            }
            ruleObj["source"] = sourceArray;
        }

        // Add targets
        if (rule.Targets.Count > 0)
        {
            var targetArray = new JsonArray();
            foreach (var target in rule.Targets)
            {
                targetArray.Add(BuildTarget(target));
            }
            ruleObj["target"] = targetArray;
        }

        // Add dependent (nested rules or group invocations)
        if (rule.Dependent is not null)
        {
            switch (rule.Dependent)
            {
                case RuleSetExpression ruleSet:
                    // Nested rules
                    var nestedRules = new JsonArray();
                    foreach (var nestedRule in ruleSet.Rules)
                    {
                        nestedRules.Add(BuildRule(nestedRule));
                    }
                    ruleObj["rule"] = nestedRules;
                    break;

                case GroupInvocationExpression groupInvocation:
                    // Group invocation
                    var dependentArray = new JsonArray
                    {
                        BuildDependent(groupInvocation)
                    };
                    ruleObj["dependent"] = dependentArray;
                    break;
            }
        }

        return ruleObj;
    }

    /// <summary>
    /// Builds a source element from a SourceExpression.
    /// </summary>
    private static JsonObject BuildSource(SourceExpression source)
    {
        var sourceObj = new JsonObject();

        // Extract context and element from qualified identifier
        var (context, element) = ExtractContextAndElement(source.Context);
        sourceObj["context"] = context;

        if (element is not null)
        {
            sourceObj["element"] = element;
        }

        // Add variable
        if (source.Variable is not null)
        {
            sourceObj["variable"] = source.Variable;
        }

        // Add type
        if (source.Type is not null)
        {
            sourceObj["type"] = source.Type;
        }

        // Add cardinality
        if (source.Cardinality is not null)
        {
            sourceObj["min"] = source.Cardinality.Min;
            sourceObj["max"] = source.Cardinality.Max.HasValue
                ? source.Cardinality.Max.Value.ToString()
                : "*";
        }

        // Add condition
        if (source.Condition is not null)
        {
            sourceObj["condition"] = ExpressionToString(source.Condition);
        }

        // Add check
        if (source.Check is not null)
        {
            sourceObj["check"] = ExpressionToString(source.Check);
        }

        // Add log message
        if (source.Log is not null)
        {
            sourceObj["logMessage"] = ExpressionToString(source.Log);
        }

        // Add default value
        if (source.Default is not null)
        {
            // Default values are typically strings in StructureMap
            sourceObj["defaultValueString"] = ExpressionToString(source.Default);
        }

        return sourceObj;
    }

    /// <summary>
    /// Builds a target element from a TargetExpression.
    /// </summary>
    private static JsonObject BuildTarget(TargetExpression target)
    {
        var targetObj = new JsonObject();

        // Extract context and element
        if (target.Context is not null)
        {
            var (context, element) = ExtractContextAndElement(target.Context);
            targetObj["context"] = context;

            if (element is not null)
            {
                targetObj["element"] = element;
            }
        }

        // Add variable
        if (target.Variable is not null)
        {
            targetObj["variable"] = target.Variable;
        }

        // Add transform and parameters
        if (target.Transform is not null)
        {
            switch (target.Transform)
            {
                case TransformExpression transform:
                    targetObj["transform"] = transform.FunctionName;

                    if (transform.Arguments.Count > 0)
                    {
                        var paramArray = new JsonArray();
                        foreach (var arg in transform.Arguments)
                        {
                            paramArray.Add(BuildParameter(arg));
                        }
                        targetObj["parameter"] = paramArray;
                    }
                    break;

                case LiteralExpression literal:
                    // Direct assignment - use 'copy' transform with the value
                    targetObj["transform"] = "copy";
                    var literalParam = new JsonArray
                    {
                        BuildParameter(literal)
                    };
                    targetObj["parameter"] = literalParam;
                    break;

                case IdentifierExpression identifier:
                    // Variable reference - use 'copy' transform
                    targetObj["transform"] = "copy";
                    var idParam = new JsonArray
                    {
                        new JsonObject { ["valueId"] = identifier.Name }
                    };
                    targetObj["parameter"] = idParam;
                    break;

                case QualifiedIdentifierExpression qualifiedId:
                    // Qualified reference - use 'copy' transform
                    targetObj["transform"] = "copy";
                    var qualParam = new JsonArray
                    {
                        new JsonObject { ["valueString"] = ExpressionToString(qualifiedId) }
                    };
                    targetObj["parameter"] = qualParam;
                    break;
            }
        }

        // Add list mode
        if (target.ListMode.HasValue)
        {
            targetObj["listMode"] = new JsonArray { ListModeToString(target.ListMode.Value) };
        }

        return targetObj;
    }

    /// <summary>
    /// Builds a dependent element from a GroupInvocationExpression.
    /// </summary>
    private static JsonObject BuildDependent(GroupInvocationExpression invocation)
    {
        var dependent = new JsonObject
        {
            ["name"] = invocation.GroupName
        };

        if (invocation.Arguments.Count > 0)
        {
            var paramArray = new JsonArray();
            foreach (var arg in invocation.Arguments)
            {
                paramArray.Add(BuildParameter(arg));
            }
            dependent["variable"] = paramArray;  // FHIR spec uses 'variable' for dependent parameters
        }

        return dependent;
    }

    /// <summary>
    /// Builds a parameter object from an expression.
    /// </summary>
    private static JsonObject BuildParameter(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => literal.Value switch
            {
                string str => new JsonObject { ["valueString"] = str },
                int i => new JsonObject { ["valueInteger"] = i },
                decimal d => new JsonObject { ["valueDecimal"] = (double)d },
                bool b => new JsonObject { ["valueBoolean"] = b },
                _ => new JsonObject { ["valueString"] = literal.Value.ToString() }
            },
            IdentifierExpression identifier => new JsonObject { ["valueId"] = identifier.Name },
            QualifiedIdentifierExpression qualifiedId => new JsonObject { ["valueString"] = ExpressionToString(qualifiedId) },
            _ => new JsonObject { ["valueString"] = ExpressionToString(expression) }
        };
    }

    /// <summary>
    /// Extracts context and element from a qualified identifier expression.
    /// For example: "src.name" → context="src", element="name"
    /// For simple identifier: "src" → context="src", element=null
    /// </summary>
    private static (string context, string? element) ExtractContextAndElement(Expression expression)
    {
        return expression switch
        {
            QualifiedIdentifierExpression qualified when qualified.Context is IdentifierExpression id =>
                (id.Name, qualified.Property),

            QualifiedIdentifierExpression qualified =>
                // Nested qualified identifier - flatten to string
                (ExpressionToString(qualified.Context), qualified.Property),

            IdentifierExpression identifier =>
                (identifier.Name, null),

            _ =>
                (ExpressionToString(expression), null)
        };
    }

    /// <summary>
    /// Converts an expression to a string representation.
    /// </summary>
    private static string ExpressionToString(Expression expression)
    {
        return expression switch
        {
            FhirPathExpression fhirPath => fhirPath.PathExpression,
            IdentifierExpression identifier => identifier.Name,
            QualifiedIdentifierExpression qualified => $"{ExpressionToString(qualified.Context)}.{qualified.Property}",
            IndexExpression index => $"{ExpressionToString(index.Context)}[{index.Index}]",
            LiteralExpression literal => literal.Value.ToString() ?? "",
            _ => expression.ToString() ?? ""
        };
    }

    /// <summary>
    /// Converts a ModelMode enum to its FHIR string representation.
    /// </summary>
    private static string ModelModeToString(ModelMode mode) => mode switch
    {
        ModelMode.Source => "source",
        ModelMode.Target => "target",
        ModelMode.Queried => "queried",
        ModelMode.Produced => "produced",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid model mode")
    };

    /// <summary>
    /// Converts a ParameterMode enum to its FHIR string representation.
    /// </summary>
    private static string ParameterModeToString(ParameterMode mode) => mode switch
    {
        ParameterMode.Source => "source",
        ParameterMode.Target => "target",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid parameter mode")
    };

    /// <summary>
    /// Converts a ListMode enum to its FHIR string representation.
    /// </summary>
    private static string ListModeToString(ListMode mode) => mode switch
    {
        ListMode.First => "first",
        ListMode.NotFirst => "not_first",
        ListMode.Last => "last",
        ListMode.NotLast => "not_last",
        ListMode.OnlyOne => "only_one",
        ListMode.Share => "share",
        ListMode.Single => "single",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid list mode")
    };
}
