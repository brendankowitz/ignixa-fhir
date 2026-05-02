// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Processor that shifts date and dateTime values by a deterministic offset derived from a cryptographic key.
/// </summary>
public class DateShiftProcessor : IDeIdProcessor
{
    public string DateShiftKey { get; }

    public string DateShiftKeyPrefix { get; }

    public int? DateShiftFixedOffsetInDays { get; }

    public bool EnablePartialDatesForRedact { get; }

    public DateShiftProcessor(
        string dateShiftKey,
        string dateShiftKeyPrefix,
        bool enablePartialDatesForRedact,
        int? dateShiftFixedOffsetInDays = null)
    {
        EnsureArg.IsNotNullOrWhiteSpace(dateShiftKey, nameof(dateShiftKey));
        EnsureArg.IsNotNull(dateShiftKeyPrefix, nameof(dateShiftKeyPrefix));

        DateShiftKey = dateShiftKey;
        DateShiftKeyPrefix = dateShiftKeyPrefix;
        EnablePartialDatesForRedact = enablePartialDatesForRedact;
        DateShiftFixedOffsetInDays = dateShiftFixedOffsetInDays;
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var (wasModified, operationType) = ProcessCore(node, context.ResourceId);

            var newResult = new ProcessorResult
            {
                WasModified = wasModified,
                OperationType = operationType,
                ProcessedPaths = wasModified ? [node.Location ?? string.Empty] : []
            };

            return ValueTask.FromResult(Result<ProcessorResult>.Success(newResult));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Failure(new DeIdError(
                "PROCESSOR_ERROR",
                $"Failed to process node: {ex.Message}",
                Exception: ex,
                Path: node.Location)));
        }
    }

    private (bool WasModified, string OperationType) ProcessCore(IElement node, string resourceId)
    {
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return (false, DeIdOperations.DateShift);
        }

        var effectivePrefix = DateShiftKeyPrefix;
        if (string.IsNullOrEmpty(effectivePrefix))
        {
            effectivePrefix = resourceId;
        }

        if (node.IsDateNode())
        {
            var result = DateTimeTool.ShiftDateNode(node, DateShiftKey, effectivePrefix, DateShiftFixedOffsetInDays, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsDateTimeNode() || node.IsInstantNode())
        {
            var result = DateTimeTool.ShiftDateTimeAndInstantNode(node, DateShiftKey, effectivePrefix, DateShiftFixedOffsetInDays, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        return (false, DeIdOperations.DateShift);
    }
}
