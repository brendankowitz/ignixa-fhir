// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Processors;

/// <summary>
/// Processor that preserves element values without modification, marking them as explicitly retained.
/// </summary>
public class KeepProcessor : IAnonymizerProcessor
{
    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        EnsureArg.IsNotNull(node, nameof(node));

        var newResult = new ProcessorResult
        {
            WasModified = false,
            OperationType = AnonymizationOperations.Keep,
            ProcessedPaths = ImmutableArray<string>.Empty
        };

        return ValueTask.FromResult(Result<ProcessorResult>.Success(newResult));
    }
}
