// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Tests.Utilities.Processors;

internal sealed class UpperCaseProcessor : IAnonymizerProcessor
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
