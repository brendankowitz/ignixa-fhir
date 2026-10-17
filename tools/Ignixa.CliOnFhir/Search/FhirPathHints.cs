// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Checks a search's <c>columns</c> and <c>fhirpath</c> with the static analyzer (<see cref="FhirPathAnalyzer"/>) before they
/// are evaluated, and turns what it decides into hints (ADR 2610). An element no type on the path declares never yields a
/// value, so the call fails with what the type does declare; a path the analyzer proves always empty is a warning that rides
/// along with the results.
/// </summary>
/// <remarks>
/// It must never refuse an expression the evaluator answers, so it blocks only on a decided miss: an unresolved property on a
/// concrete type that is not one of its choice element's instance names (<c>valueQuantity</c> is answered by the evaluator,
/// though FHIRPath spells it <c>value.ofType(Quantity)</c>). Everything the analyzer cannot decide (after <c>resolve()</c> or an
/// unknown function such as <c>patientSearch()</c>) and its other findings raise nothing. On an abstract Resource focus
/// (<c>entry.resource</c> of the <c>fhirpath</c> Bundle, <c>contained</c>) the name must be declared by no resource type at all.
/// </remarks>
internal static class FhirPathHints
{
    /// <summary>The warning code of a path the analyzer proves always empty.</summary>
    public const string NeverYieldsCode = "fhirpath-never-yields";

    private const int MaxSuggestions = 3;

    private static readonly ConditionalWeakTable<IFhirSchemaProvider, FhirPathAnalyzer> Analyzers = [];

    /// <summary>The warnings of every column, each column analysed with the searched type as its root.</summary>
    /// <exception cref="ToolRequestException">A column names an element its type does not declare.</exception>
    public static IReadOnlyList<IssueItem> ForColumns(IFhirSchemaProvider schema, string resourceType, IReadOnlyList<ColumnSearch.CompiledColumn> columns) =>
        [.. columns.Where(column => column.Expression is not null).SelectMany(column => Check(schema, $"Column '{column.Name}'", column.Expression!, resourceType, resourceType))];

    /// <summary>The warnings of a <c>fhirpath</c> expression, analysed over the searchset Bundle whose entries are <paramref name="resourceType"/>.</summary>
    /// <exception cref="ToolRequestException">The expression names an element its type does not declare.</exception>
    public static IReadOnlyList<IssueItem> ForFhirPath(IFhirSchemaProvider schema, string resourceType, Expression expression) =>
        Check(schema, "'fhirpath'", expression, "Bundle", resourceType);

    private static List<IssueItem> Check(IFhirSchemaProvider schema, string label, Expression expression, string root, string resourceType)
    {
        FhirPathAnalyzer analyzer = Analyzers.GetValue(schema, static schema => new FhirPathAnalyzer(schema));
        AnalysisResult result;
        // The analyzer keeps no per-call state on itself, but it does not document thread safety; MCP calls run concurrently.
        lock (analyzer)
        {
            result = analyzer.Analyze(expression, root);
        }

        List<IssueItem> warnings = [];
        foreach (ValidationIssue issue in result.Issues)
        {
            if (issue.IsUnresolvedProperty && Miss(schema, issue, resourceType) is { } hint)
            {
                throw ToolRequestException.Error("invalid-parameter", $"{label} {hint}");
            }

            if (issue.IsAlwaysEmpty)
            {
                warnings.Add(new IssueItem("warning", NeverYieldsCode, $"{label} can never yield a value here: {issue.Message}", Location: null));
            }
        }

        return warnings;
    }

    /// <summary>Why the issue's property never yields a value, with what to write instead; null when it might yield one.</summary>
    private static string? Miss(IFhirSchemaProvider schema, ValidationIssue issue, string resourceType)
    {
        string property = issue.Property!;
        List<IType> concrete = [];
        foreach (string name in issue.FocusTypes)
        {
            if (schema.GetTypeDefinition(name) is not { } type)
            {
                return null;
            }

            if (type.Info is { IsAbstract: true, IsResource: true })
            {
                // Any resource may be there at runtime: only a name no resource type has is decided, and it is described
                // against the searched type, which is what entry.resource holds.
                if (schema.ResourceTypeNames.Select(schema.GetTypeDefinition).Any(candidate => candidate is not null && Declares(candidate, property)))
                {
                    return null;
                }

                concrete.Add(schema.GetTypeDefinition(resourceType) ?? type);
                continue;
            }

            concrete.Add(type);
        }

        return concrete.Count == 0 || concrete.Any(type => Declares(type, property)) ? null : Describe(concrete[0], property);
    }

    /// <summary>Whether <paramref name="type"/> has <paramref name="property"/>: an element, or a choice element's instance name (<c>valueQuantity</c>).</summary>
    private static bool Declares(IType type, string property) =>
        type.Children.Any(child => child.Info.Name == property)
        || ChoiceElements(type).Any(choice => choice.Codes.Any(code => choice.Base + Capitalized(code) == property));

    private static string Describe(IType type, string property)
    {
        string typeName = type.Info.Name;
        foreach ((string choiceBase, IReadOnlyList<string> codes) in ChoiceElements(type))
        {
            if (property.Length > choiceBase.Length && property.StartsWith(choiceBase, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = property[choiceBase.Length..];
                string best = codes.OrderBy(code => Distance(suffix.ToUpperInvariant(), code.ToUpperInvariant())).First();
                return $"'{QueryEcho.Value(property)}' is not an element of {typeName}: {choiceBase} is one of {string.Join(", ", codes)}. Write {choiceBase}.ofType({best}) (or {choiceBase}{Capitalized(best)}).";
            }
        }

        List<string> names = [.. type.Children.Select(child => child.Info.Name.Replace("[x]", string.Empty, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];
        int tolerance = Math.Max(2, property.Length / 3);
        List<string> near = [.. names
            .Select(name => (Name: name, Distance: Distance(property.ToUpperInvariant(), name.ToUpperInvariant())))
            .Where(candidate => candidate.Distance <= tolerance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(candidate => $"'{candidate.Name}'")];
        return near.Count > 0
            ? $"'{QueryEcho.Value(property)}' is not an element of {typeName}, so it never yields a value; did you mean {string.Join(" or ", near)}?"
            : $"'{QueryEcho.Value(property)}' is not an element of {typeName}, so it never yields a value.";
    }

    private static IEnumerable<(string Base, IReadOnlyList<string> Codes)> ChoiceElements(IType type) =>
        type.Children
            .Where(child => child.Info.IsChoiceElement || child.Info.Name.EndsWith("[x]", StringComparison.Ordinal))
            .Select(child => (
                Base: child.Info.Name.Replace("[x]", string.Empty, StringComparison.Ordinal),
                Codes: (IReadOnlyList<string>)[.. (child as ITypeExtended)?.Types.Select(reference => reference.Code) ?? []]))
            .Where(choice => choice.Codes.Count > 0);

    private static string Capitalized(string code) => code.Length == 0 ? code : char.ToUpperInvariant(code[0]) + code[1..];

    /// <summary>The Levenshtein distance between two names.</summary>
    private static int Distance(string a, string b)
    {
        int[] previous = [.. Enumerable.Range(0, b.Length + 1)];
        for (int i = 1; i <= a.Length; i++)
        {
            int[] current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
