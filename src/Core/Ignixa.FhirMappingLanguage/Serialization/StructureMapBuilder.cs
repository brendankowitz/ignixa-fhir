/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Builds a FHIR StructureMap resource from a MapExpression AST.
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Ignixa.Serialization.SourceNodes;

// Ignixa.Models also defines an "Expression" datatype (FHIR's Expression, e.g. used by DataRequirement) --
// disambiguate in favor of this AST's own Expression, used pervasively throughout this file.
using Expression = Ignixa.FhirMappingLanguage.Expressions.Expression;

namespace Ignixa.FhirMappingLanguage.Serialization;

/// <summary>
/// Builds a FHIR StructureMap resource from a MapExpression AST.
/// Enables conversion: AST → StructureMap Resource.
/// </summary>
public class StructureMapBuilder
{
    private readonly FhirVersion _targetVersion;

    /// <summary>
    /// Initializes a new instance of the StructureMapBuilder.
    /// </summary>
    /// <param name="targetVersion">The target FHIR version for the generated StructureMap (defaults to R5).</param>
    public StructureMapBuilder(FhirVersion targetVersion = FhirVersion.R5)
    {
        _targetVersion = targetVersion;
    }

    /// <summary>
    /// Builds a FHIR StructureMap resource from a MapExpression AST.
    /// </summary>
    /// <param name="map">The parsed map expression.</param>
    /// <returns>A StructureMap representing the StructureMap resource.</returns>
    public StructureMap Build(MapExpression map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var structureMap = new StructureMap
        {
            Url = map.Url,
            Name = map.Identifier,
            Status = PublicationStatus.Active
        };

        // CRITICAL: Set FHIR version FIRST before accessing any child properties
        // This enables version-specific validation and prevents NotSupportedException
        structureMap.FhirVersion = _targetVersion;

        // Add structures (uses declarations)
        foreach (var uses in map.Uses)
        {
            structureMap.Structure.Add(BuildStructure(uses));
        }

        // Add imports
        foreach (var import in map.Imports)
        {
            structureMap.Import.Add(import.Url);
        }

        // Add contained ConceptMaps
        foreach (var conceptMap in map.ConceptMaps)
        {
            structureMap.Contained.Add(BuildConceptMapResource(conceptMap, map.Url));
        }

        // Add groups
        foreach (var group in map.Groups)
        {
            structureMap.Group.Add(BuildGroup(group));
        }

        return structureMap;
    }

    /// <summary>
    /// Builds a structure element from a UsesExpression.
    /// </summary>
    private StructureMapStructure BuildStructure(UsesExpression uses)
    {
        var structure = new StructureMapStructure
        {
            Url = uses.Url,
            Alias = uses.Alias,
            Mode = ConvertToModelMode(uses.Mode)
        };
        structure.FhirVersion = _targetVersion;
        return structure;
    }

    /// <summary>
    /// Builds a group element from a GroupExpression.
    /// </summary>
    private StructureMapGroup BuildGroup(GroupExpression group)
    {
        var groupNode = new StructureMapGroup
        {
            Name = group.Name,
            Extends = group.Extends
        };

        // Inherit FhirVersion from builder
        groupNode.FhirVersion = _targetVersion;

        // typeMode is required in R4/R4B (map-group-type-mode value set has "none" as a valid default);
        // R5 dropped "none" from the value set entirely (only types|type-and-types remain) and made the
        // element optional, so omit it there rather than writing a value R5 doesn't accept.
        if (_targetVersion < FhirVersion.R5)
        {
            groupNode.MutableNode["typeMode"] = "none";
        }

        // Add input parameters
        foreach (var param in group.Parameters)
        {
            groupNode.Input.Add(BuildInput(param));
        }

        // Add rules
        foreach (var rule in group.Rules)
        {
            groupNode.Rule.Add(BuildRule(rule));
        }

        return groupNode;
    }

    /// <summary>
    /// Builds an input element from a ParameterExpression.
    /// </summary>
    private StructureMapGroupInput BuildInput(ParameterExpression parameter)
    {
        var input = new StructureMapGroupInput
        {
            Name = parameter.Name,
            Type = parameter.Type,
            Mode = ConvertToInputMode(parameter.Mode)
        };
        input.FhirVersion = _targetVersion;
        return input;
    }

    /// <summary>
    /// Builds a rule element from a RuleExpression.
    /// </summary>
    private StructureMapGroupRule BuildRule(RuleExpression rule)
    {
        var ruleNode = new StructureMapGroupRule
        {
            Name = rule.Name
        };

        // Inherit FhirVersion from builder
        ruleNode.FhirVersion = _targetVersion;

        // Add sources
        foreach (var source in rule.Sources)
        {
            ruleNode.Source.Add(BuildSource(source));
        }

        // Add targets
        foreach (var target in rule.Targets)
        {
            ruleNode.Target.Add(BuildTarget(target));
        }

        // Add dependent (nested rules or group invocations)
        if (rule.Dependent is not null)
        {
            switch (rule.Dependent)
            {
                case RuleSetExpression ruleSet:
                    // Nested rules
                    foreach (var nestedRule in ruleSet.Rules)
                    {
                        ruleNode.Rule.Add(BuildRule(nestedRule));
                    }
                    break;

                case GroupInvocationExpression groupInvocation:
                    // Group invocation
                    ruleNode.Dependent.Add(BuildDependent(groupInvocation));
                    break;
            }
        }

        return ruleNode;
    }

    /// <summary>
    /// Builds a source element from a SourceExpression.
    /// </summary>
    private StructureMapGroupRuleSource BuildSource(SourceExpression source)
    {
        var (context, element) = ExtractContextAndElement(source.Context);

        var sourceNode = new StructureMapGroupRuleSource
        {
            Context = context,
            Element = element,
            Variable = source.Variable,
            Type = source.Type
        };

        // Inherit FhirVersion from builder
        sourceNode.FhirVersion = _targetVersion;

        // Add cardinality
        if (source.Cardinality is not null)
        {
            sourceNode.Min = source.Cardinality.Min;
            sourceNode.Max = source.Cardinality.Max.HasValue
                ? source.Cardinality.Max.Value.ToString()
                : "*";
        }

        // Add condition
        if (source.Condition is not null)
        {
            sourceNode.Condition = ExpressionToString(source.Condition);
        }

        // Add check
        if (source.Check is not null)
        {
            sourceNode.Check = ExpressionToString(source.Check);
        }

        // Add log message
        if (source.Log is not null)
        {
            sourceNode.LogMessage = ExpressionToString(source.Log);
        }

        // Add default value using version-aware extension method
        if (source.Default is not null)
        {
            sourceNode.SetDefaultValueString(ExpressionToString(source.Default));
        }

        return sourceNode;
    }

    /// <summary>
    /// Builds a target element from a TargetExpression.
    /// </summary>
    private StructureMapGroupRuleTarget BuildTarget(TargetExpression target)
    {
        var targetNode = new StructureMapGroupRuleTarget
        {
            Variable = target.Variable
        };

        // Inherit FhirVersion from builder
        targetNode.FhirVersion = _targetVersion;

        // Extract context and element
        if (target.Context is not null)
        {
            var (context, element) = ExtractContextAndElement(target.Context);
            targetNode.SetContext(context);
            targetNode.Element = element;
        }

        // Add transform and parameters
        if (target.Transform is not null)
        {
            switch (target.Transform)
            {
                case TransformExpression transform:
                    if (TryParseTransformName(transform.FunctionName, out var transformEnum))
                    {
                        targetNode.Transform = transformEnum;
                    }

                    foreach (var arg in transform.Arguments)
                    {
                        var param = BuildParameter(arg);
                        param.FhirVersion = _targetVersion;
                        targetNode.Parameter.Add(param);
                    }
                    break;

                case LiteralExpression literal:
                    // Direct assignment - use 'copy' transform with the value
                    targetNode.Transform = MapTransform.Copy;
                    var literalParam = BuildParameter(literal);
                    literalParam.FhirVersion = _targetVersion;
                    targetNode.Parameter.Add(literalParam);
                    break;

                case IdentifierExpression identifier:
                    // Variable reference - use 'copy' transform
                    targetNode.Transform = MapTransform.Copy;
                    var identifierParam = new StructureMapGroupRuleTargetParameter();
                    identifierParam.FhirVersion = _targetVersion;
                    identifierParam.SetValue("Id", JsonValue.Create(identifier.Name));
                    targetNode.Parameter.Add(identifierParam);
                    break;

                case QualifiedIdentifierExpression qualifiedId:
                    // Qualified reference - use 'copy' transform
                    targetNode.Transform = MapTransform.Copy;
                    var qualParam = new StructureMapGroupRuleTargetParameter();
                    qualParam.FhirVersion = _targetVersion;
                    qualParam.SetValue("String", JsonValue.Create(ExpressionToString(qualifiedId)));
                    targetNode.Parameter.Add(qualParam);
                    break;
            }
        }

        // Add list mode
        if (target.ListMode.HasValue)
        {
            targetNode.ListMode.Add(ConvertToListModeString(target.ListMode.Value));
        }

        return targetNode;
    }

    /// <summary>
    /// Builds a dependent element from a GroupInvocationExpression.
    /// </summary>
    private StructureMapGroupRuleDependent BuildDependent(GroupInvocationExpression invocation)
    {
        var dependent = new StructureMapGroupRuleDependent
        {
            Name = invocation.GroupName
        };

        // Inherit FhirVersion from builder
        dependent.FhirVersion = _targetVersion;

        // Add arguments using version-appropriate method. Dependent.parameter/variable aren't on the
        // shared base (R4 has variable, R5+ has parameter) so both branches go through MutableNode
        // directly rather than a typed list property.
        if (_targetVersion >= FhirVersion.R5)
        {
            // R5+: Use structured parameters (preserves the argument's real type, e.g. an int literal
            // stays valueInteger rather than being flattened to a string).
            var parameterArray = (JsonArray)(dependent.MutableNode["parameter"] ??= new JsonArray());
            foreach (var arg in invocation.Arguments)
            {
                var param = BuildParameter(arg);
                param.FhirVersion = _targetVersion;
                parameterArray.Add(param.MutableNode);
            }
        }
        else
        {
            // R4/R4B: Use simple string variables
            foreach (var arg in invocation.Arguments)
            {
                dependent.AddDependentVariable(ExpressionToString(arg));
            }
        }

        return dependent;
    }

    /// <summary>
    /// Builds a parameter object from an expression.
    /// </summary>
    private static StructureMapGroupRuleTargetParameter BuildParameter(Expression expression)
    {
        var param = new StructureMapGroupRuleTargetParameter();

        switch (expression)
        {
            case LiteralExpression literal:
                switch (literal.Value)
                {
                    case string str:
                        param.SetValue("String", JsonValue.Create(str));
                        break;
                    case int i:
                        param.SetValue("Integer", JsonValue.Create(i));
                        break;
                    case decimal d:
                        param.SetValue("Decimal", JsonValue.Create((double)d));
                        break;
                    case bool b:
                        param.SetValue("Boolean", JsonValue.Create(b));
                        break;
                    default:
                        param.SetValue("String", JsonValue.Create(literal.Value.ToString()));
                        break;
                }
                break;

            case IdentifierExpression identifier:
                param.SetValue("Id", JsonValue.Create(identifier.Name));
                break;

            case QualifiedIdentifierExpression qualifiedId:
                param.SetValue("String", JsonValue.Create(ExpressionToString(qualifiedId)));
                break;

            default:
                param.SetValue("String", JsonValue.Create(ExpressionToString(expression)));
                break;
        }

        return param;
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
    /// Converts ModelMode to MapModelMode.
    /// </summary>
    private static MapModelMode ConvertToModelMode(ModelMode mode) => mode switch
    {
        ModelMode.Source => MapModelMode.Source,
        ModelMode.Target => MapModelMode.Target,
        ModelMode.Queried => MapModelMode.Queried,
        ModelMode.Produced => MapModelMode.Produced,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid model mode")
    };

    /// <summary>
    /// Converts ParameterMode to MapInputMode.
    /// </summary>
    private static MapInputMode ConvertToInputMode(ParameterMode mode) => mode switch
    {
        ParameterMode.Source => MapInputMode.Source,
        ParameterMode.Target => MapInputMode.Target,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid parameter mode")
    };

    /// <summary>
    /// Converts ListMode to its FHIR string representation.
    /// </summary>
    private static string ConvertToListModeString(ListMode mode) => mode switch
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

    /// <summary>
    /// Tries to parse a transform function name to MapTransform enum.
    /// </summary>
    private static bool TryParseTransformName(string functionName, out MapTransform transform)
    {
        var parsed = EnumUtility.ParseLiteral<MapTransform>(functionName);
        transform = parsed ?? MapTransform.Copy;
        return parsed.HasValue;
    }

    /// <summary>
    /// Builds a contained ConceptMap resource from a ConceptMapDeclarationExpression.
    /// </summary>
    private static ResourceJsonNode BuildConceptMapResource(ConceptMapDeclarationExpression conceptMap, string structureMapUrl)
    {
        // Extract ID from the identifier (remove leading # if present)
        var id = conceptMap.Identifier.StartsWith('#')
            ? conceptMap.Identifier.Substring(1)
            : conceptMap.Identifier;

        var resource = new ResourceJsonNode
        {
            ResourceType = "ConceptMap",
            Id = id
        };

        // Use MutableNode to add ConceptMap-specific properties
        resource.MutableNode["status"] = JsonValue.Create("active");

        // Build groups with element mappings
        if (conceptMap.Groups.Count > 0)
        {
            var groupArray = new JsonArray();

            foreach (var group in conceptMap.Groups)
            {
                var groupObj = new JsonObject();

                // Find source and target URLs from prefixes
                if (group.SourceSystem is not null)
                {
                    groupObj["source"] = group.SourceSystem;
                }

                if (group.TargetSystem is not null)
                {
                    groupObj["target"] = group.TargetSystem;
                }

                // Build elements from code mappings
                if (group.CodeMaps.Count > 0)
                {
                    // Group code maps by source prefix to determine source/target URLs
                    var prefixUrls = conceptMap.Prefixes.ToDictionary(
                        p => p.PrefixName,
                        p => p.Url);

                    var elementArray = new JsonArray();

                    // Group by source code to create element entries
                    var bySource = group.CodeMaps.GroupBy(m => (m.SourcePrefix, m.SourceCode));
                    foreach (var sourceGroup in bySource)
                    {
                        var elementObj = new JsonObject
                        {
                            ["code"] = sourceGroup.Key.SourceCode
                        };

                        var targetArray = new JsonArray();
                        foreach (var mapping in sourceGroup)
                        {
                            var targetObj = new JsonObject
                            {
                                ["code"] = mapping.TargetCode,
                                ["equivalence"] = ConvertToEquivalenceString(mapping.Equivalence)
                            };
                            targetArray.Add(targetObj);
                        }

                        elementObj["target"] = targetArray;
                        elementArray.Add(elementObj);
                    }

                    // Set source/target from first mapping's prefixes if not already set
                    var firstMap = group.CodeMaps.Count > 0 ? group.CodeMaps[0] : null;
                    if (firstMap is not null)
                    {
                        if (!groupObj.ContainsKey("source") && prefixUrls.TryGetValue(firstMap.SourcePrefix, out var sourceUrl))
                        {
                            groupObj["source"] = sourceUrl;
                        }

                        if (!groupObj.ContainsKey("target") && prefixUrls.TryGetValue(firstMap.TargetPrefix, out var targetUrl))
                        {
                            groupObj["target"] = targetUrl;
                        }
                    }

                    groupObj["element"] = elementArray;
                }

                groupArray.Add(groupObj);
            }

            resource.MutableNode["group"] = groupArray;
        }

        return resource;
    }

    /// <summary>
    /// Converts ConceptMapEquivalence to FHIR string representation.
    /// </summary>
    private static string ConvertToEquivalenceString(ConceptMapEquivalence equivalence) => equivalence switch
    {
        ConceptMapEquivalence.Equivalent => "equivalent",
        ConceptMapEquivalence.RelatedTo => "relatedto",
        ConceptMapEquivalence.Broader => "wider",
        ConceptMapEquivalence.Narrower => "narrower",
        ConceptMapEquivalence.NotRelatedTo => "unmatched",
        _ => "equivalent"
    };
}
