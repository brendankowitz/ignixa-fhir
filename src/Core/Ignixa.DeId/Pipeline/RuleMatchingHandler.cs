// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Handler that matches FHIRPath rules from configuration against the resource.
/// Populates context.MatchedRules with rules that apply to the current resource.
/// </summary>
internal sealed class RuleMatchingHandler(ILogger<RuleMatchingHandler> logger) : DeIdPipelineHandler
{
    /// <inheritdoc />
    public override async ValueTask<Result<DeIdResult>> InvokeAsync(
        DeIdContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken)
    {
        var resourceType = context.Resource.ResourceType;
        logger.LogDebug(
            "Matching rules for resource type {ResourceType} with {RuleCount} configured rules",
            resourceType,
            context.Options.Rules.Length);

        foreach (var rule in context.Options.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!RuleAppliesToResource(rule.ResourceType, resourceType))
            {
                logger.LogTrace(
                    "Skipping rule {RulePath} - resource type filter {RuleType} does not match {ResourceType}",
                    rule.Path,
                    rule.ResourceType,
                    resourceType);
                continue;
            }

            try
            {
                var matchedElements = context.Element.Select(rule.Path);

                if (matchedElements.Count > 0)
                {
                    logger.LogDebug(
                        "Rule {RulePath} matched {MatchCount} elements",
                        rule.Path,
                        matchedElements.Count);

                    context.MatchedRules.Add(new MatchedRule
                    {
                        Rule = rule,
                        MatchedElements = matchedElements
                    });
                }
                else
                {
                    logger.LogTrace("Rule {RulePath} matched no elements", rule.Path);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to evaluate FHIRPath expression '{Path}': {Message}",
                    rule.Path,
                    ex.Message);

                context.AddWarning($"Failed to evaluate FHIRPath expression '{rule.Path}': {ex.Message}");
            }
        }

        logger.LogDebug(
            "Rule matching complete: {MatchedCount} rules matched",
            context.MatchedRules.Count);

        return await nextHandler(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines if a rule applies to a given resource type.
    /// </summary>
    private static bool RuleAppliesToResource(string? ruleResourceType, string resourceType)
    {
        if (string.IsNullOrEmpty(ruleResourceType))
        {
            return true;
        }

        return string.Equals(ruleResourceType, resourceType, StringComparison.OrdinalIgnoreCase)
               || string.Equals(ruleResourceType, "Resource", StringComparison.OrdinalIgnoreCase);
    }
}
