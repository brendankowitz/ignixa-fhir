// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Core.UnitTests;

internal class MaskProcessor : IAnonymizerProcessor
{
    private readonly int _maskedLength;

    public MaskProcessor(JsonObject setting)
    {
        _maskedLength = int.Parse(setting["maskedLength"]?.ToString() ?? "0");
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        if (node.Value == null)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Success(
                new ProcessorResult
                {
                    WasModified = false,
                    OperationType = "MASK",
                    ProcessedPaths = []
                }));
        }

        var mask = new string('*', _maskedLength);
        var currentValue = node.Value.ToString() ?? string.Empty;
        var newValue = currentValue.Length > _maskedLength ? mask + currentValue[_maskedLength..] : mask;
        node.SetValue(newValue);

        return ValueTask.FromResult(Result<ProcessorResult>.Success(
            new ProcessorResult
            {
                WasModified = true,
                OperationType = "MASK",
                ProcessedPaths = [node.Location ?? string.Empty]
            }));
    }
}
