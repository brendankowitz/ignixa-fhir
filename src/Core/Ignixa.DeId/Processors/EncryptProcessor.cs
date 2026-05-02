// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Processor that encrypts element values using AES-CBC and encodes the result as Base64.
/// </summary>
public class EncryptProcessor : IDeIdProcessor
{
    private readonly byte[] _key;
    private readonly ILogger _logger = DeIdLogging.CreateLogger<EncryptProcessor>();

    public EncryptProcessor(string encryptKey)
    {
        EnsureArg.IsNotNullOrWhiteSpace(encryptKey, nameof(encryptKey));

        var keyBytes = Encoding.UTF8.GetBytes(encryptKey);

        // AES requires 128, 192, or 256-bit keys (16, 24, or 32 bytes)
        if (keyBytes.Length != 16 && keyBytes.Length != 24 && keyBytes.Length != 32)
        {
            throw new ArgumentException(
                $"Encryption key must be 16, 24, or 32 bytes (128, 192, or 256 bits) when UTF-8 encoded. " +
                $"Provided key is {keyBytes.Length} bytes. For 256-bit AES security, use a 32-character ASCII string.",
                nameof(encryptKey));
        }

        _key = keyBytes;
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
                OperationType = DeIdOperations.Encrypt,
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

    private bool ProcessCore(IElement node)
    {
        if (string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return false;
        }

        var input = node.Value.ToString()!;
        ElementMutationTool.SetValue(node, EncryptTool.EncryptTextToBase64WithAes(input, _key));
        _logger.LogDebug("De-identified value at '{Location}' using Encrypt", node.Location);

        return true;
    }
}
