// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Tools;

namespace Ignixa.Anonymizer.Processors;

/// <summary>
/// Processor that replaces element values with their HMAC-SHA256 hash using a configurable key.
/// </summary>
public class CryptoHashProcessor : IAnonymizerProcessor
{
    private readonly string _cryptoHashKey;
    private readonly IFhirSchemaProvider _schema;
    private readonly Func<string, string> _cryptoHashFunction;
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<CryptoHashProcessor>();

    public CryptoHashProcessor(string cryptoHashKey, IFhirSchemaProvider schema)
    {
        EnsureArg.IsNotNullOrWhiteSpace(cryptoHashKey, nameof(cryptoHashKey));
        EnsureArg.IsNotNull(schema, nameof(schema));

        _cryptoHashKey = cryptoHashKey;
        _schema = schema;
        _cryptoHashFunction = input => CryptoHashTool.ComputeHmacSHA256Hash(input, _cryptoHashKey);
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var wasModified = ProcessCore(node);

            var newResult = new ProcessorResult
            {
                WasModified = wasModified,
                OperationType = AnonymizationOperations.CryptoHash,
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

    private bool ProcessCore(IElement node)
    {
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return false;
        }

        var input = node.Value.ToString()!;

        if (node.IsReferenceStringNode(parent: null))
        {
            var newReference = ReferenceTool.TransformReferenceId(input, _schema, _cryptoHashFunction);
            ElementMutationTool.SetValue(node, newReference);
        }
        else
        {
            ElementMutationTool.SetValue(node, _cryptoHashFunction(input));
        }

        _logger.LogDebug("Anonymized value at '{Location}' using CryptoHash", node.Location);

        return true;
    }
}
