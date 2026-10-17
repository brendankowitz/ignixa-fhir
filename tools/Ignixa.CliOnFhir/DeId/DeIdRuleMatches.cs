// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.DeId.Configuration;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>
/// How many elements each rule's path matched in the resources written, so that a rule that matched elements its method then changed
/// none of (a <c>generalize</c> whose cases fit nothing and that keeps other values, a <c>substitute</c> with nothing to put) is not
/// silent: those elements were written as they were. <c>Ignixa.DeId</c> counts operations per label, not per rule, so a method is
/// checked against the labels it can count under, and methods that share one (<c>dateShift</c> and <c>perturb</c>) are checked
/// together. <c>keep</c> changes nothing by design and is not checked. Not thread-safe.
/// </summary>
/// <param name="rules">The configuration's rules, in its order.</param>
internal sealed class DeIdRuleMatches(IReadOnlyList<FhirPathRule> rules)
{
    // The labels each method's own changes are counted under (a redaction a method falls back to is counted as redact, and is not
    // counted here, so that a redact rule's changes do not hide another method's having changed nothing).
    private static readonly Dictionary<string, string[]> Labels = new(StringComparer.Ordinal)
    {
        ["redact"] = ["REDACT", "ABSTRACT"],
        ["dateShift"] = ["DATESHIFT", "PERTURB"],
        ["cryptoHash"] = ["CRYPTOHASH"],
        ["encrypt"] = ["ENCRYPT"],
        ["substitute"] = ["SUBSTITUTE"],
        ["perturb"] = ["PERTURB"],
        ["generalize"] = ["GENERALIZE"],
    };

    private readonly long[] _matched = new long[rules.Count];

    /// <summary>How many elements each rule matches in <paramref name="resource"/>, before it is de-identified; null when a path could not be evaluated on it.</summary>
    public long[]? Count(IElement resource, string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resource);

        long[] counts = new long[rules.Count];
        for (int i = 0; i < rules.Count; i++)
        {
            FhirPathRule rule = rules[i];
            if (rule.ResourceType is { Length: > 0 } type && !string.Equals(type, resourceType, StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "Resource", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                counts[i] = resource.Select(rule.Path).Count();
            }
            catch (Exception ex) when (ex is FhirPathEvaluationException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or InvalidCastException)
            {
                // The library fails the same rule on this resource, which is then not written, so its matches are never added.
                return null;
            }
        }

        return counts;
    }

    /// <summary>Adds the matches of a resource that was written.</summary>
    public void Add(long[] counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        for (int i = 0; i < counts.Length; i++)
        {
            _matched[i] += counts[i];
        }
    }

    /// <summary>A warning for each method whose rules matched elements of which the library changed none, naming those rules.</summary>
    public IEnumerable<IssueItem> Unapplied(IReadOnlyDictionary<string, long> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        foreach (IGrouping<string, int> method in Enumerable.Range(0, rules.Count).GroupBy(i => DeIdConfiguration.MethodName(rules[i].Method)))
        {
            if (!Labels.TryGetValue(method.Key, out string[]? labels) || labels.Any(label => operations.GetValueOrDefault(DeIdConfiguration.MethodName(label)) > 0))
            {
                continue;
            }

            List<int> matched = [.. method.Where(i => _matched[i] > 0)];
            if (matched.Count == 0)
            {
                continue;
            }

            string named = string.Join(", ", matched.Select(i => $"fhirPathRules[{i}] (path digest {FhirIds.Digest(rules[i].Path)}, {_matched[i]} elements)"));
            yield return new IssueItem("warning", "deid-rule-unapplied", $"The {method.Key} rules {named} matched elements in the resources written, but Ignixa.DeId counted no {method.Key} change, so those elements may have been written as they were. Check the rules' settings.", Location: null);
        }
    }
}
