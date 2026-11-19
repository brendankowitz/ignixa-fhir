// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.FhirPath;
using Ignixa.DeId.Configuration.ProcessorSettings;
using Ignixa.DeId.Exceptions;
using Ignixa.DeId.Extensions;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Rejects de-identification configurations whose rule paths cannot be parsed or whose Generalize or Perturb
/// settings are invalid.
/// </summary>
/// <remarks>
/// A rule that cannot be parsed can never match, so accepting it would leave the data it was meant
/// to protect in clear. Only parse-time failures are detectable here; a path that parses but throws
/// for particular resources is caught at evaluation time by the rule matching handler.
/// </remarks>
internal static class FhirPathRuleValidator
{
    /// <exception cref="ConfigurationException">A rule path, Generalize case or Perturb setting is not valid.</exception>
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
            FhirPathExtensions.EnsureParses(rule.Path, requestSupplied: !rule.IsServerDefined);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // Any parse failure, including InsufficientExecutionStackException from an over-deep path,
            // means the rule can never be evaluated. OutOfMemoryException is process-critical and not caught.
            throw new ConfigurationException(
                $"Rule path '{LogSafeText.Sanitize(rule.Path)}' is not a valid FHIRPath expression: {LogSafeText.Sanitize(ex.Message)}",
                ex);
        }
    }

    // Generalize cases are FHIRPath expressions evaluated per node; validating them here rejects a bad case at
    // load rather than failing (or, under LogAndContinue, being skipped) at request time. Perturb defaults a
    // missing span to zero, which returns the value unchanged as a successful perturbation.
    private static void EnsureSettingsValid(FhirPathRule rule)
    {
        Action<Dictionary<string, object>>? validate = rule.Method?.ToUpperInvariant() switch
        {
            "GENERALIZE" => GeneralizeSetting.ValidateRuleSettings,
            "PERTURB" => PerturbSetting.ValidateRuleSettings,
            _ => null
        };

        if (validate is null)
        {
            return;
        }

        var settings = new Dictionary<string, object>(
            rule.Settings ?? ImmutableDictionary<string, object>.Empty)
        {
            [Constants.PathKey] = rule.Path,
            [Constants.MethodKey] = rule.Method
        };

        validate(settings);
    }
}
