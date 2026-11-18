// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.FhirPath;
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

        // Captured once: rule matching runs before any de-identification mutation, so every rule
        // can safely share one snapshot instead of paying for a fresh re-derive per rule (context.Element
        // re-derives on every read so that later pipeline stages never observe a stale, pre-mutation tree).
        var rootElement = context.Element;

        foreach (var rule in context.Options.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!RuleAppliesToResource(rule.ResourceType, resourceType))
            {
                logger.LogTrace(
                    "Skipping rule {RulePath} - resource type filter {RuleType} does not match {ResourceType}",
                    LogSafeText.Sanitize(rule.Path),
                    rule.ResourceType,
                    resourceType);
                continue;
            }

            IReadOnlyList<IElement> matchedElements;
            try
            {
                matchedElements = rootElement.Select(rule.Path, requestSupplied: !rule.IsServerDefined, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                // The expression is operator-supplied and the library reports bad ones as FormatException (it does
                // not parse or exceeds the parser's limits), FhirPathEvaluationException (it does not evaluate
                // against this resource), FhirPathResourceLimitExceededException, or
                // InsufficientExecutionStackException from an over-deep path, so every such failure is a rule that
                // could not be evaluated. OutOfMemoryException is process-critical and deliberately not caught.
                // Fail closed: skipping an unevaluable rule would return the data it targets in clear.
                // Logged by type and stack only: an evaluation message can quote resource values.
                logger.LogError(
                    "Failed to evaluate FHIRPath rule '{Path}' ({CauseType}){NewLine}{StackTrace}",
                    LogSafeText.Sanitize(rule.Path),
                    ex.GetType().Name,
                    Environment.NewLine,
                    ex.StackTrace);

                return Result<DeIdResult>.Failure(new DeIdError(
                    "RULE_EVALUATION_FAILED",
                    $"Rule '{LogSafeText.Sanitize(rule.Path)}' could not be evaluated against {resourceType}: {LogSafeText.Sanitize(ex.Message)}",
                    ErrorSeverity.Error,
                    ex));
            }

            // An empty match is legitimate (the element is absent); only an evaluation failure is an error.
            if (matchedElements.Count == 0)
            {
                logger.LogTrace("Rule {RulePath} matched no elements", LogSafeText.Sanitize(rule.Path));
                continue;
            }

            logger.LogDebug(
                "Rule {RulePath} matched {MatchCount} elements",
                LogSafeText.Sanitize(rule.Path),
                matchedElements.Count);

            context.MatchedRules.Add(new MatchedRule
            {
                Rule = rule,
                MatchedElements = matchedElements
            });
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
