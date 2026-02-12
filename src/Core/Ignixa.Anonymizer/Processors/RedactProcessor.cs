// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class RedactProcessor : IAnonymizerProcessor
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
            return ValueTask.FromResult(Result<ProcessorResult>.Failure(new AnonymizerError(
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
            return (false, AnonymizationOperations.Redact);
        }

        if (node.IsDateNode())
        {
            var result = DateTimeUtility.RedactDateNode(node, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsDateTimeNode() || node.IsInstantNode())
        {
            var result = DateTimeUtility.RedactDateTimeAndInstantNode(node, EnablePartialDatesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsAgeDecimalNode(parent: null))
        {
            var result = DateTimeUtility.RedactAgeDecimalNode(node, EnablePartialAgesForRedact);
            return (result.WasModified, result.OperationType);
        }

        if (node.IsPostalCodeNode())
        {
            var result = PostalCodeUtility.RedactPostalCode(node, EnablePartialZipCodesForRedact, RestrictedZipCodeTabulationAreas);
            return (result.WasModified, result.OperationType);
        }

        ElementMutationHelper.ClearValue(node);
        return (true, AnonymizationOperations.Redact);
    }
}
