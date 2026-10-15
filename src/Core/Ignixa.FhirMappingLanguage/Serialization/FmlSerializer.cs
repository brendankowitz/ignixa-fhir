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

        SerializeMetadata(map, context);

        // ConceptMap declarations
        foreach (var conceptMap in map.ConceptMaps)
        {
            SerializeConceptMap(conceptMap, context);
        }

        if (map.ConceptMaps.Count > 0)
        {
            context.AppendLine();
        }

        // An explicit header preserves identity when metadata does not supply the same URL and name.
        if (!map.Metadata.TryGetValue("url", out string? metadataUrl) || metadataUrl != map.Url ||
            !map.Metadata.TryGetValue("name", out string? metadataName) || metadataName != map.Identifier)
        {
            context.AppendLine($"map {FmlText.Quote(map.Url)} = {FmlText.Quote(map.Identifier, '"')}");
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

    private static void SerializeMetadata(MapExpression map, SerializationContext context)
    {
        if (map.MetadataDeclarations.Count > 0)
        {
            foreach (var declaration in map.MetadataDeclarations)
            {
                context.AppendLine($"/// {declaration.Key} = {declaration.Value}");
            }
        }
        else
        {
            foreach (var metadata in map.Metadata)
            {
                context.AppendLine($"/// {metadata.Key} = {FmlText.Quote(metadata.Value)}");
            }
        }

        if (map.MetadataDeclarations.Count > 0 || map.Metadata.Count > 0)
        {
            context.AppendLine();
        }
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
            context.AppendLine($"uses {FmlText.Quote(uses.Url)} alias {FmlText.Identifier(uses.Alias)} as {mode}");
        }
        else
        {
            context.AppendLine($"uses {FmlText.Quote(uses.Url)} as {mode}");
        }
    }

    /// <summary>
    /// Serializes an imports declaration.
    /// Format: imports "url"
    /// </summary>
    private void SerializeImports(ImportsExpression imports, SerializationContext context)
    {
        context.AppendLine($"imports {FmlText.Quote(imports.Url)}");
    }

    /// <summary>
    /// Serializes a constant declaration.
    /// Format: let NAME = value;
    /// </summary>
    private void SerializeConstant(ConstantDeclarationExpression constant, SerializationContext context)
    {
        context.Append($"let {FmlText.Identifier(constant.Name)} = ");
        if (constant.Value is FhirPathExpression fhirPath)
        {
            context.Append(fhirPath.PathExpression);
        }
        else
        {
            SerializeExpression(constant.Value, context);
        }

        context.AppendLine(";");
    }

    /// <summary>
    /// Serializes a ConceptMap declaration.
    /// Format: conceptmap "#id" { prefixes codeMaps }
    /// </summary>
    private void SerializeConceptMap(ConceptMapDeclarationExpression conceptMap, SerializationContext context)
    {
        context.AppendLine($"conceptmap {FmlText.Quote(conceptMap.Identifier)} {{");
        context.IncreaseIndent();

        // Prefixes
        foreach (var prefix in conceptMap.Prefixes)
        {
            context.AppendLine($"prefix {FmlText.Identifier(prefix.PrefixName)} = {FmlText.Quote(prefix.Url)}");
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
                    ConceptMapEquivalence.Equivalent => "-",
                    ConceptMapEquivalence.RelatedTo => "~=",
                    ConceptMapEquivalence.NotRelatedTo => "!=",
                    ConceptMapEquivalence.Broader => "<-",
                    ConceptMapEquivalence.Narrower => "->",
                    _ => throw new InvalidOperationException($"Unknown ConceptMap equivalence: {codeMap.Equivalence}")
                };
                context.AppendLine($"{FmlText.Identifier(codeMap.SourcePrefix)}:{FmlText.Quote(codeMap.SourceCode)} {equivOp} {FmlText.Identifier(codeMap.TargetPrefix)}:{FmlText.Quote(codeMap.TargetCode)}");
            }
        }

        context.DecreaseIndent();
        context.AppendLine("}");
    }

    /// <summary>
    /// Serializes a group definition.
    /// Format: group Name(parameters) [extends Base] [&lt;&lt;types&gt;&gt;|&lt;&lt;type+&gt;&gt;] { rules }
    /// </summary>
    private void SerializeGroup(GroupExpression group, SerializationContext context)
    {
        context.Append($"group {FmlText.Identifier(group.Name)}(");

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
            context.Append($" extends {FmlText.Identifier(group.Extends)}");
        }

        // Type-mode annotation
        if (group.TypeMode != GroupTypeMode.None)
        {
            context.Append(group.TypeMode == GroupTypeMode.TypeAndTypes ? " <<type+>>" : " <<types>>");
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

        context.Append($"{mode} {FmlText.Identifier(parameter.Name)}");

        if (parameter.Type is not null)
        {
            context.Append($" : {FmlText.Identifier(parameter.Type)}");
        }
    }

    /// <summary>
    /// Serializes a rule.
    /// Format: sources -> targets dependent "name";
    /// </summary>
    private void SerializeRule(RuleExpression rule, SerializationContext context)
    {
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

        if (rule.Name is not null)
        {
            context.Append($" {FmlText.Quote(rule.Name, '"')}");
        }

        context.AppendLine(";");
    }

    /// <summary>
    /// Serializes a source expression.
    /// Format: context : Type cardinality default (value) listMode as variable where (condition) check (check) log (log)
    /// </summary>
    private void SerializeSource(SourceExpression source, SerializationContext context)
    {
        // Context
        SerializeExpression(source.Context, context);

        // Type
        if (source.Type is not null)
        {
            context.Append($" : {FmlText.Identifier(source.Type)}");
        }

        // Cardinality
        if (source.Cardinality is not null)
        {
            context.Append($" {SerializeCardinality(source.Cardinality)}");
        }

        if (source.Default is not null)
        {
            context.Append(" default ");
            SerializeParenthesized(source.Default, context);
        }

        if (source.ListMode is not null)
        {
            context.Append($" {SerializeListMode(source.ListMode.Value)}");
        }

        if (source.Variable is not null)
        {
            context.Append($" as {FmlText.Identifier(source.Variable)}");
        }

        // Where clause
        if (source.Condition is not null)
        {
            context.Append(" where ");
            SerializeParenthesized(source.Condition, context);
        }

        // Check clause
        if (source.Check is not null)
        {
            context.Append(" check ");
            SerializeParenthesized(source.Check, context);
        }

        // Log clause
        if (source.Log is not null)
        {
            context.Append(" log ");
            SerializeParenthesized(source.Log, context);
        }
    }

    /// <summary>
    /// Serializes a target expression.
    /// Format: context = transform as variable listMode
    /// </summary>
    private void SerializeTarget(TargetExpression target, SerializationContext context)
    {
        // Context (optional for variable-only targets)
        if (target.Context is not null)
        {
            SerializeExpression(target.Context, context);
        }

        // Transform
        if (target.Transform is not null)
        {
            if (target.Context is not null)
            {
                context.Append(" = ");
                SerializeExpression(target.Transform, context);
            }
            else if (target.Transform is TransformExpression or GroupInvocationExpression)
            {
                SerializeExpression(target.Transform, context);
            }
            else
            {
                SerializeParenthesized(target.Transform, context);
            }
        }

        if (target.Variable is not null)
        {
            context.Append(target.Context is not null || target.Transform is not null ? " as " : "");
            context.Append(FmlText.Identifier(target.Variable));
        }

        // List mode
        if (target.ListMode is not null)
        {
            context.Append($" {SerializeListMode(target.ListMode.Value)}");
        }

        if (target.ListRuleId is not null)
        {
            context.Append($" {FmlText.Identifier(target.ListRuleId)}");
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

            case DependentExpression clauses:
                for (int i = 0; i < clauses.Invocations.Count; i++)
                {
                    if (i > 0)
                    {
                        context.Append(", ");
                    }

                    SerializeGroupInvocation(clauses.Invocations[i], context);
                }

                if (clauses.Rules is not null)
                {
                    if (clauses.Invocations.Count > 0)
                    {
                        context.Append(" ");
                    }

                    SerializeRuleSet(clauses.Rules, context);
                }

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
        context.Append($"{FmlText.Identifier(invocation.GroupName)}(");

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
                context.Append(FmlText.Identifier(identifier.Name));
                break;

            case FhirPathExpression fhirPath:
                context.Append($"({fhirPath.PathExpression})");
                break;

            case QualifiedIdentifierExpression qualified:
                SerializeExpression(qualified.Context, context);
                context.Append($".{FmlText.Identifier(qualified.Property)}");
                break;

            case IndexExpression index:
                SerializeExpression(index.Context, context);
                context.Append($"[{index.Index}]");
                break;

            case TransformExpression transform:
                SerializeTransform(transform, context);
                break;

            case GroupInvocationExpression invocation:
                SerializeGroupInvocation(invocation, context);
                break;

            case NamedArgumentExpression argument:
                context.Append($"{FmlText.Identifier(argument.Name)}: ");
                SerializeExpression(argument.Value, context);
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
                context.Append(FmlText.Quote(str));
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
        context.Append($"{FmlText.Identifier(transform.FunctionName)}(");

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

    private void SerializeParenthesized(Expression expression, SerializationContext context)
    {
        context.Append("(");
        if (expression is FhirPathExpression fhirPath)
        {
            context.Append(fhirPath.PathExpression);
        }
        else
        {
            SerializeExpression(expression, context);
        }

        context.Append(")");
    }

    private static string SerializeListMode(ListMode listMode)
    {
        return listMode switch
        {
            ListMode.First => "first",
            ListMode.NotFirst => "not_first",
            ListMode.Last => "last",
            ListMode.NotLast => "not_last",
            ListMode.OnlyOne => "only_one",
            ListMode.Share => "share",
            ListMode.Single => "single",
            _ => throw new InvalidOperationException($"Unknown list mode: {listMode}")
        };
    }
}
