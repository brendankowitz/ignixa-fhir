// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Processors;

namespace Ignixa.DeId.Tests.Utilities.Processors;

internal sealed class UpperCaseProcessor : IDeIdProcessor
{
    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        if (node.Value is null)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Success(
                new ProcessorResult
                {
                    WasModified = false,
                    OperationType = "UPPERCASE",
                    ProcessedPaths = []
                }));
        }

        var currentValue = node.Value.ToString() ?? string.Empty;
        node.SetValue(currentValue.ToUpperInvariant());

        return ValueTask.FromResult(Result<ProcessorResult>.Success(
            new ProcessorResult
            {
                WasModified = true,
                OperationType = "UPPERCASE",
                ProcessedPaths = [node.Location ?? string.Empty]
            }));
    }
}
