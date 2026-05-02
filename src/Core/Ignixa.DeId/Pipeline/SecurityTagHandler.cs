// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;
using Ignixa.DeId.Processors;
using Microsoft.Extensions.Logging;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Handler that updates security labels based on operations performed.
/// Maps operation names to the corresponding security label flags.
/// </summary>
internal sealed class SecurityTagHandler(ILogger<SecurityTagHandler> logger) : DeIdPipelineHandler
{
    /// <inheritdoc />
    public override async ValueTask<Result<DeIdResult>> InvokeAsync(
        DeIdContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken)
    {
        if (context.OperationCounts.Count == 0)
        {
            logger.LogDebug("No operations performed, skipping security label update");
            return await nextHandler(context, cancellationToken).ConfigureAwait(false);
        }

        logger.LogDebug(
            "Updating security labels based on {OperationCount} operation types",
            context.OperationCounts.Count);

        context.SecurityLabels = BuildSecurityLabels(context.OperationCounts);

        LogAppliedLabels(context.SecurityLabels);

        return await nextHandler(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds security labels from operation counts.
    /// </summary>
    private static AppliedSecurityLabels BuildSecurityLabels(Dictionary<string, int> operationCounts)
    {
        return new AppliedSecurityLabels
        {
            IsRedacted = HasOperation(operationCounts, DeIdOperations.Redact),
            IsAbstracted = HasOperation(operationCounts, DeIdOperations.Abstract),
            IsCryptoHashed = HasOperation(operationCounts, DeIdOperations.CryptoHash),
            IsEncrypted = HasOperation(operationCounts, DeIdOperations.Encrypt),
            IsPerturbed = HasOperation(operationCounts, DeIdOperations.Perturb),
            IsSubstituted = HasOperation(operationCounts, DeIdOperations.Substitute),
            IsGeneralized = HasOperation(operationCounts, DeIdOperations.Generalize)
        };
    }

    /// <summary>
    /// Checks if an operation was performed.
    /// </summary>
    private static bool HasOperation(Dictionary<string, int> operationCounts, string operation)
    {
        return operationCounts.TryGetValue(operation.ToUpperInvariant(), out var count) && count > 0;
    }

    /// <summary>
    /// Logs which security labels were applied.
    /// </summary>
    private void LogAppliedLabels(AppliedSecurityLabels labels)
    {
        var appliedLabels = new List<string>();

        if (labels.IsRedacted)
            appliedLabels.Add("REDACTED");
        if (labels.IsAbstracted)
            appliedLabels.Add("ABSTRACTED");
        if (labels.IsCryptoHashed)
            appliedLabels.Add("CRYPTOHASHED");
        if (labels.IsEncrypted)
            appliedLabels.Add("ENCRYPTED");
        if (labels.IsPerturbed)
            appliedLabels.Add("PERTURBED");
        if (labels.IsSubstituted)
            appliedLabels.Add("SUBSTITUTED");
        if (labels.IsGeneralized)
            appliedLabels.Add("GENERALIZED");

        if (appliedLabels.Count > 0)
        {
            logger.LogDebug(
                "Applied security labels: {Labels}",
                string.Join(", ", appliedLabels));
        }
    }
}
