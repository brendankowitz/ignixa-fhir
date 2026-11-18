// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.DeId.Configuration.ProcessorSettings;
using Ignixa.DeId.Exceptions;
using Ignixa.DeId.Extensions;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Rejects de-identification configurations whose rule paths or processor FHIRPath settings cannot be parsed.
/// </summary>
/// <remarks>
/// A rule that cannot be parsed can never match, so accepting it would leave the data it was meant
/// to protect in clear. Only parse-time failures are detectable here; a path that parses but throws
/// for particular resources is caught at evaluation time by the rule matching handler.
/// </remarks>
internal static class FhirPathRuleValidator
{
    /// <exception cref="ConfigurationException">A rule path or Generalize case is not valid.</exception>
    public static void EnsureValid(ImmutableArray<FhirPathRule> rules)
    {
        foreach (var rule in rules)
        {
            EnsurePathParses(rule);
            EnsureSettingsValid(rule);
        }
    }

    private static void EnsurePathParses(FhirPathRule rule)
    {
        try
        {
            FhirPathExtensions.EnsureParses(rule.Path);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // Any parse failure, including InsufficientExecutionStackException from an over-deep path,
            // means the rule can never be evaluated. OutOfMemoryException is process-critical and not caught.
            throw new ConfigurationException(
                $"Rule path '{rule.Path}' is not a valid FHIRPath expression: {ex.Message}",
                ex);
        }
    }

    // Generalize cases are FHIRPath expressions evaluated per node; validating them here rejects a bad case at
    // load rather than failing (or, under LogAndContinue, being skipped) at request time.
    private static void EnsureSettingsValid(FhirPathRule rule)
    {
        if (!string.Equals(rule.Method, "GENERALIZE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var settings = new Dictionary<string, object>(
            rule.Settings ?? ImmutableDictionary<string, object>.Empty)
        {
            [Constants.PathKey] = rule.Path,
            [Constants.MethodKey] = rule.Method
        };

        GeneralizeSetting.ValidateRuleSettings(settings);
    }
}
