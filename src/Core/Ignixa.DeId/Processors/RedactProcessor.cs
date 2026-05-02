// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tools;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Processor that redacts FHIR element values, with support for partial redaction of dates, ages, and postal codes.
/// </summary>
public class RedactProcessor : IDeIdProcessor
{
    public bool EnablePartialDatesForRedact { get; }

    public bool EnablePartialAgesForRedact { get; }

    public bool EnablePartialZipCodesForRedact { get; }

    public List<string>? RestrictedZipCodeTabulationAreas { get; }

    public RedactProcessor(
        bool enablePartialDatesForRedact,
        bool enablePartialAgesForRedact,
        bool enablePartialZipCodesForRedact,
        List<string>? restrictedZipCodeTabulationAreas)
    {
        EnablePartialDatesForRedact = enablePartialDatesForRedact;
        EnablePartialAgesForRedact = enablePartialAgesForRedact;
        EnablePartialZipCodesForRedact = enablePartialZipCodesForRedact;
        RestrictedZipCodeTabulationAreas = restrictedZipCodeTabulationAreas;
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var (wasModified, operationType) = ProcessCore(node);

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

    private (bool WasModified, string OperationType) ProcessCore(IElement node)
    {
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return (false, DeIdOperations.Redact);
        }

        if (node.IsDateNode())
        {
            var result = DateTimeTool.RedactDateNode(node, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsDateTimeNode() || node.IsInstantNode())
        {
            var result = DateTimeTool.RedactDateTimeAndInstantNode(node, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsAgeDecimalNode(parent: null))
        {
            var result = DateTimeTool.RedactAgeDecimalNode(node, EnablePartialAgesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsPostalCodeNode())
        {
            var result = PostalCodeTool.RedactPostalCode(node, EnablePartialZipCodesForRedact, RestrictedZipCodeTabulationAreas);
            return (result.WasModified, result.OperationType);
        }

        ElementMutationTool.RemoveProperty(node);
        return (true, DeIdOperations.Redact);
    }
}
