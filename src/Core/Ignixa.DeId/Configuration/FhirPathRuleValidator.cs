// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.DeId.Exceptions;
using Ignixa.DeId.Extensions;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Rejects de-identification configurations whose rule paths cannot be parsed.
/// </summary>
/// <remarks>
/// A rule that cannot be parsed can never match, so accepting it would leave the data it was meant
/// to protect in clear. Only parse-time failures are detectable here; a path that parses but throws
/// for particular resources is caught at evaluation time by the rule matching handler.
/// </remarks>
internal static class FhirPathRuleValidator
{
    /// <exception cref="ConfigurationException">A rule path is not a valid FHIRPath expression.</exception>
    public static void EnsureValid(ImmutableArray<FhirPathRule> rules)
    {
        foreach (var rule in rules)
        {
            try
            {
                FhirPathExtensions.EnsureParses(rule.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ConfigurationException(
                    $"Rule path '{rule.Path}' is not a valid FHIRPath expression: {ex.Message}",
                    ex);
            }
        }
    }
}
