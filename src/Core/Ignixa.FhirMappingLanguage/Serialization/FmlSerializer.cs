/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Serializes FHIR Mapping Language AST back to FML text format.
 * Enables round-trip conversion: FML Text → AST → FML Text.
 */

using System.Globalization;
using System.Linq;
using System.Text;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Serialization;

/// <summary>
/// Serializes a MapExpression AST back to FML text format.
/// Enables round-trip conversion: FML Text → AST → FML Text.
/// </summary>
public class FmlSerializer
{
    private readonly FmlSerializerOptions _options;

    public FmlSerializer(FmlSerializerOptions? options = null)
    {
        _options = options ?? FmlSerializerOptions.Default;
    }

    /// <summary>
    /// Serializes a complete map expression to FML text.
    /// </summary>
    public string Serialize(MapExpression map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var sb = new StringBuilder();
        var context = new SerializationContext(sb, _options, indentLevel: 0);

        // Header: map 'url' = 'identifier'
        context.AppendLine($"map '{EscapeStringLiteral(map.Url)}' = '{EscapeStringLiteral(map.Identifier)}'");
        context.AppendLine();

        // ConceptMap declarations
        foreach (var conceptMap in map.ConceptMaps)
        {
            SerializeConceptMap(conceptMap, context);
        }

        if (map.ConceptMaps.Count > 0)
        {
            context.AppendLine();
        }

        // Uses declarations
        foreach (var uses in map.Uses)
        {
            SerializeUses(uses, context);
        }

        if (map.Uses.Count > 0)
        {
            context.AppendLine();
        }

        // Imports declarations
        foreach (var import in map.Imports)
        {
            SerializeImports(import, context);
        }

        if (map.Imports.Count > 0)
        {
            context.AppendLine();
        }

        // Constant declarations
        foreach (var constant in map.Constants)
        {
            SerializeConstant(constant, context);
        }

        if (map.Constants.Count > 0)
        {
            context.AppendLine();
        }

        // Groups
        for (int i = 0; i < map.Groups.Count; i++)
        {
            if (i > 0)
            {
                context.AppendLine();
            }

            SerializeGroup(map.Groups[i], context);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Serializes a uses declaration.
    /// Format: uses "url" alias AliasName as mode
    /// </summary>
    private void SerializeUses(UsesExpression uses, SerializationContext context)
    {
        var mode = uses.Mode switch
        {
            ModelMode.Source => "source",
            ModelMode.Queried => "queried",
            ModelMode.Target => "target",
            ModelMode.Produced => "produced",
            _ => throw new InvalidOperationException($"Unknown model mode: {uses.Mode}")
        };

        if (uses.Alias is not null)
        {
            context.AppendLine($"uses '{EscapeStringLiteral(uses.Url)}' alias {uses.Alias} as {mode}");
        }
        else
        {
            context.AppendLine($"uses '{EscapeStringLiteral(uses.Url)}' as {mode}");
        }
    }

    /// <summary>
    /// Serializes an imports declaration.
    /// Format: imports "url"
    /// </summary>
    private void SerializeImports(ImportsExpression imports, SerializationContext context)
    {
        context.AppendLine($"imports '{EscapeStringLiteral(imports.Url)}'");
    }

    /// <summary>
    /// Serializes a constant declaration.
    /// Format: constant NAME = value
    /// </summary>
    private void SerializeConstant(ConstantDeclarationExpression constant, SerializationContext context)
    {
        context.Append($"constant {constant.Name} = ");
        SerializeExpression(constant.Value, context);
        context.AppendLine();
    }

    /// <summary>
    /// Serializes a ConceptMap declaration.
    /// Format: conceptmap "#id" { prefixes codeMaps }
    /// </summary>
    private void SerializeConceptMap(ConceptMapDeclarationExpression conceptMap, SerializationContext context)
    {
        context.AppendLine($"conceptmap '{EscapeStringLiteral(conceptMap.Identifier)}' {{");
        context.IncreaseIndent();

        // Prefixes
        foreach (var prefix in conceptMap.Prefixes)
        {
            context.AppendLine($"prefix {prefix.PrefixName} = '{EscapeStringLiteral(prefix.Url)}'");
        }

        if (conceptMap.Prefixes.Count > 0 && conceptMap.Groups.Any(g => g.CodeMaps.Count > 0))
        {
            context.AppendLine();
        }

        // Code mappings
        foreach (var group in conceptMap.Groups)
        {
            foreach (var codeMap in group.CodeMaps)
            {
                var equivOp = codeMap.Equivalence switch
                {
                    ConceptMapEquivalence.Equivalent => "==",
                    ConceptMapEquivalence.RelatedTo => "~=",
                    ConceptMapEquivalence.NotRelatedTo => "!=",
                    ConceptMapEquivalence.Broader => "<-",
                    ConceptMapEquivalence.Narrower => "->",
                    _ => "=="
                };
                context.AppendLine($"{codeMap.SourcePrefix}:{codeMap.SourceCode} {equivOp} {codeMap.TargetPrefix}:{codeMap.TargetCode}");
            }
        }

        context.DecreaseIndent();
        context.AppendLine("}");
    }

    /// <summary>
    /// Serializes a group definition.
    /// Format: group Name(parameters) extends Base { rules }
    /// </summary>
    private void SerializeGroup(GroupExpression group, SerializationContext context)
    {
        context.Append($"group {group.Name}(");

        // Parameters
        for (int i = 0; i < group.Parameters.Count; i++)
        {
            if (i > 0)
            {
                context.Append(", ");
            }

            SerializeParameter(group.Parameters[i], context);
        }

        context.Append(")");

        // Extends clause
        if (group.Extends is not null)
        {
            context.Append($" extends {group.Extends}");
        }

        // Rules
        if (group.Rules.Count == 0)
        {
            context.AppendLine(" {");
            context.AppendLine("}");
        }
        else
        {
            context.AppendLine(" {");
            context.IncreaseIndent();

            for (int i = 0; i < group.Rules.Count; i++)
            {
                SerializeRule(group.Rules[i], context);

                // Add blank line between rules for readability
                if (i < group.Rules.Count - 1)
                {
                    context.AppendLine();
                }
            }

            context.DecreaseIndent();
            context.AppendLine("}");
        }
    }

    /// <summary>
    /// Serializes a parameter.
    /// Format: mode name : Type
    /// </summary>
    private void SerializeParameter(ParameterExpression parameter, SerializationContext context)
    {
        var mode = parameter.Mode switch
        {
            ParameterMode.Source => "source",
            ParameterMode.Target => "target",
            _ => throw new InvalidOperationException($"Unknown parameter mode: {parameter.Mode}")
        };

        context.Append($"{mode} {parameter.Name}");

        if (parameter.Type is not null)
        {
            context.Append($" : {parameter.Type}");
        }
    }

    /// <summary>
    /// Serializes a rule.
    /// Format: name : sources -> targets dependent;
    /// </summary>
    private void SerializeRule(RuleExpression rule, SerializationContext context)
    {
        // Rule name (optional)
        if (rule.Name is not null)
        {
            context.Append($"{rule.Name} : ");
        }

        // Sources
        for (int i = 0; i < rule.Sources.Count; i++)
        {
            if (i > 0)
            {
                context.Append(", ");
            }

            SerializeSource(rule.Sources[i], context);
        }

        // Arrow and targets
        if (rule.Targets.Count > 0)
        {
            context.Append(" -> ");

            for (int i = 0; i < rule.Targets.Count; i++)
            {
                if (i > 0)
                {
                    context.Append(", ");
                }

                SerializeTarget(rule.Targets[i], context);
            }
        }

        // Dependent clause (then ...)
        if (rule.Dependent is not null)
        {
            context.Append(" ");
            SerializeDependent(rule.Dependent, context);
        }

        context.AppendLine(";");
    }

    /// <summary>
    /// Serializes a source expression.
    /// Format: context as variable : Type cardinality where (condition) check (check) log (log) default value
    /// </summary>
    private void SerializeSource(SourceExpression source, SerializationContext context)
    {
        // Context
        SerializeExpression(source.Context, context);

        // Variable
        if (source.Variable is not null)
        {
            context.Append($" as {source.Variable}");
        }

        // Type
        if (source.Type is not null)
        {
            context.Append($" : {source.Type}");
        }

        // Cardinality
        if (source.Cardinality is not null)
        {
            context.Append($" {SerializeCardinality(source.Cardinality)}");
        }

        // Where clause
        if (source.Condition is not null)
        {
            context.Append(" where ");
            SerializeExpression(source.Condition, context);
        }

        // Check clause
        if (source.Check is not null)
        {
            context.Append(" check ");
            SerializeExpression(source.Check, context);
        }

        // Log clause
        if (source.Log is not null)
        {
            context.Append(" log ");
            SerializeExpression(source.Log, context);
        }

        // Default value
        if (source.Default is not null)
        {
            context.Append(" default ");
            SerializeExpression(source.Default, context);
        }
    }

    /// <summary>
    /// Serializes a target expression.
    /// Format: context as variable = transform listMode
    /// </summary>
    private void SerializeTarget(TargetExpression target, SerializationContext context)
    {
        // Context (optional for variable-only targets)
        if (target.Context is not null)
        {
            SerializeExpression(target.Context, context);
        }

        // Variable
        if (target.Variable is not null)
        {
            if (target.Context is not null)
            {
                context.Append($" as {target.Variable}");
            }
            else
            {
                context.Append(target.Variable);
            }
        }

        // Transform
        if (target.Transform is not null)
        {
            context.Append(" = ");
            SerializeExpression(target.Transform, context);
        }

        // List mode
        if (target.ListMode is not null)
        {
            var listMode = target.ListMode.Value switch
            {
                ListMode.First => "first",
                ListMode.NotFirst => "not_first",
                ListMode.Last => "last",
                ListMode.NotLast => "not_last",
                ListMode.OnlyOne => "only_one",
                ListMode.Share => "share",
                ListMode.Single => "single",
                _ => throw new InvalidOperationException($"Unknown list mode: {target.ListMode}")
            };

            context.Append($" {listMode}");
        }
    }

    /// <summary>
    /// Serializes a dependent clause (then ...).
    /// </summary>
    private void SerializeDependent(Expression dependent, SerializationContext context)
    {
        context.Append("then ");

        switch (dependent)
        {
            case GroupInvocationExpression invocation:
                SerializeGroupInvocation(invocation, context);
                break;

            case RuleSetExpression ruleSet:
                SerializeRuleSet(ruleSet, context);
                break;

            default:
                throw new InvalidOperationException($"Unexpected dependent expression type: {dependent.GetType().Name}");
        }
    }

    /// <summary>
    /// Serializes a group invocation.
    /// Format: GroupName(args)
    /// </summary>
    private void SerializeGroupInvocation(GroupInvocationExpression invocation, SerializationContext context)
    {
        context.Append($"{invocation.GroupName}(");

        for (int i = 0; i < invocation.Arguments.Count; i++)
        {
            if (i > 0)
            {
                context.Append(", ");
            }

            SerializeExpression(invocation.Arguments[i], context);
        }

        context.Append(")");
    }

    /// <summary>
    /// Serializes a rule set (nested rules).
    /// Format: { rules }
    /// </summary>
    private void SerializeRuleSet(RuleSetExpression ruleSet, SerializationContext context)
    {
        if (ruleSet.Rules.Count == 0)
        {
            context.Append("{}");
        }
        else
        {
            context.AppendLine("{");
            context.IncreaseIndent();

            for (int i = 0; i < ruleSet.Rules.Count; i++)
            {
                SerializeRule(ruleSet.Rules[i], context);

                if (i < ruleSet.Rules.Count - 1)
                {
                    context.AppendLine();
                }
            }

            context.DecreaseIndent();
            context.Append("}");
        }
    }

    /// <summary>
    /// Serializes a generic expression.
    /// </summary>
    private void SerializeExpression(Expression expression, SerializationContext context)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                SerializeLiteral(literal, context);
                break;

            case IdentifierExpression identifier:
                context.Append(identifier.Name);
                break;

            case FhirPathExpression fhirPath:
                context.Append($"({fhirPath.PathExpression})");
                break;

            case QualifiedIdentifierExpression qualified:
                SerializeExpression(qualified.Context, context);
                context.Append($".{qualified.Property}");
                break;

            case IndexExpression index:
                SerializeExpression(index.Context, context);
                context.Append($"[{index.Index}]");
                break;

            case TransformExpression transform:
                SerializeTransform(transform, context);
                break;

            default:
                throw new InvalidOperationException($"Unexpected expression type: {expression.GetType().Name}");
        }
    }

    /// <summary>
    /// Serializes a literal value.
    /// </summary>
    private void SerializeLiteral(LiteralExpression literal, SerializationContext context)
    {
        switch (literal.Value)
        {
            case string str:
                context.Append($"'{EscapeStringLiteral(str)}'");
                break;

            case bool b:
                context.Append(b ? "true" : "false");
                break;

            case int i:
                context.Append(i.ToString(CultureInfo.InvariantCulture));
                break;

            case decimal d:
                context.Append(d.ToString(CultureInfo.InvariantCulture));
                break;

            default:
                throw new InvalidOperationException($"Unsupported literal type: {literal.Value.GetType().Name}");
        }
    }

    /// <summary>
    /// Serializes a transform function call.
    /// Format: functionName(args)
    /// </summary>
    private void SerializeTransform(TransformExpression transform, SerializationContext context)
    {
        context.Append($"{transform.FunctionName}(");

        for (int i = 0; i < transform.Arguments.Count; i++)
        {
            if (i > 0)
            {
                context.Append(", ");
            }

            SerializeExpression(transform.Arguments[i], context);
        }

        context.Append(")");
    }

    /// <summary>
    /// Serializes cardinality.
    /// Format: min..max or min..* (unbounded)
    /// </summary>
    private static string SerializeCardinality(Cardinality cardinality)
    {
        return cardinality.Max.HasValue
            ? $"{cardinality.Min}..{cardinality.Max}"
            : $"{cardinality.Min}..*";
    }

    /// <summary>
    /// Escapes a string for use in single-quoted literals.
    /// FML uses '' to escape a single quote within a string.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
