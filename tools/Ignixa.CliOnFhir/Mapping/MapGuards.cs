// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Views;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Limits on a map that keep its parsing and evaluation from overflowing the stack, which ends the process and which no <c>catch</c>
/// can stop; a StructureMap of the folder is untrusted, so these hold for every map. The FML parser recurses on nested brackets and
/// <c>then { }</c> blocks, the evaluator on the depth of the parsed map (a chain <c>src.a.a.a...</c> of 50,000 steps overflows it),
/// and the FHIRPath parser on the shape of every expression in it. So the FML text is held to brackets nested at most
/// <see cref="ViewExpressionGuards.MaxDepth"/> deep, counted everywhere (quoted text and comments included, as for a ViewDefinition),
/// before the parser sees it; the parsed map to <see cref="MaxTreeDepth"/> levels, measured without recursion; and every FHIRPath
/// expression in it, and every string literal (<c>evaluate()</c> reads one as FHIRPath), to the limits of a ViewDefinition's
/// expressions (<see cref="ViewExpressionGuards"/>).
/// </summary>
internal static class MapGuards
{
    /// <summary>The deepest a parsed map may be, counting each node of its tree.</summary>
    public const int MaxTreeDepth = 256;

    /// <summary>Why the FML <paramref name="text"/> breaks a limit, or null.</summary>
    public static string? TextViolation(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int depth = 0;
        foreach (char c in text)
        {
            if (c is '(' or '[' or '{')
            {
                if (++depth > ViewExpressionGuards.MaxDepth)
                {
                    return $"has brackets nested more than {ViewExpressionGuards.MaxDepth} levels deep";
                }
            }
            else if (c is ')' or ']' or '}' && depth > 0)
            {
                depth--;
            }
        }

        return null;
    }

    /// <summary>Why the parsed <paramref name="map"/> breaks a limit, or null.</summary>
    public static string? TreeViolation(MapExpression map)
    {
        ArgumentNullException.ThrowIfNull(map);

        Stack<(Expression Node, int Depth)> pending = new([(map, 1)]);
        while (pending.TryPop(out (Expression Node, int Depth) item))
        {
            if (item.Depth > MaxTreeDepth)
            {
                return $"is nested more than {MaxTreeDepth} levels deep once parsed (a long chain such as src.a.a.a counts each step)";
            }

            if (Expression(item.Node) is { } expression && ViewExpressionGuards.Violation(expression) is { } reason)
            {
                return $"has a FHIRPath expression or string that {reason}";
            }

            foreach (Expression child in Children(item.Node))
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return null;
    }

    /// <summary>The node types <see cref="TreeViolation"/> knows the children of; a map that holds any other is refused.</summary>
    internal static IReadOnlyList<Type> KnownNodes { get; } =
    [
        typeof(MapExpression), typeof(GroupExpression), typeof(RuleExpression), typeof(RuleSetExpression), typeof(SourceExpression),
        typeof(TargetExpression), typeof(TransformExpression), typeof(GroupInvocationExpression), typeof(QualifiedIdentifierExpression),
        typeof(IndexExpression), typeof(ConstantDeclarationExpression), typeof(FhirPathExpression), typeof(LiteralExpression),
        typeof(IdentifierExpression), typeof(ParameterExpression), typeof(UsesExpression), typeof(ImportsExpression),
        typeof(DependentExpression), typeof(NamedArgumentExpression), typeof(ConceptMapDeclarationExpression), typeof(ConceptMapGroupExpression), typeof(ConceptMapCodeMapExpression), typeof(ConceptMapPrefixExpression),
    ];

    // The text a node holds that is, or may be read as, FHIRPath.
    private static string? Expression(Expression node) => node switch
    {
        FhirPathExpression fhirPath => fhirPath.PathExpression,
        LiteralExpression { Value: string text } => text,
        _ => null,
    };

    private static IEnumerable<Expression> Children(Expression node) => node switch
    {
        MapExpression map => [.. map.Groups, .. map.Constants, .. map.Uses, .. map.Imports, .. map.ConceptMaps],
        GroupExpression group => [.. group.Parameters, .. group.Rules],
        RuleExpression rule => [.. rule.Sources, .. rule.Targets, .. Present(rule.Dependent)],
        RuleSetExpression rules => rules.Rules,
        DependentExpression dependent => [.. dependent.Invocations, .. Present(dependent.Rules)],
        NamedArgumentExpression argument => [argument.Value],
        SourceExpression source => [source.Context, .. Present(source.Condition), .. Present(source.Check), .. Present(source.Log), .. Present(source.Default)],
        TargetExpression target => [.. Present(target.Context), .. Present(target.Transform)],
        TransformExpression transform => transform.Arguments,
        GroupInvocationExpression invocation => invocation.Arguments,
        QualifiedIdentifierExpression qualified => [qualified.Context],
        IndexExpression index => [index.Context],
        ConstantDeclarationExpression constant => [constant.Value],
        FhirPathExpression or LiteralExpression or IdentifierExpression or ParameterExpression or UsesExpression or ImportsExpression => [],
        ConceptMapDeclarationExpression or ConceptMapGroupExpression or ConceptMapCodeMapExpression or ConceptMapPrefixExpression => [],
        _ => throw new NotSupportedException($"The map holds a {node.GetType().Name}, which the map limits do not know how to walk."),
    };

    private static IEnumerable<Expression> Present(Expression? node) => node is null ? [] : [node];
}
