// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Core.UnitTests;

public class MockAnonymizerProcessor : IAnonymizerProcessor
{
    public MockAnonymizerProcessor(JsonObject settings)
    {
    }

    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Result<ProcessorResult>.Success(
            new ProcessorResult
            {
                WasModified = false,
                OperationType = "MOCK",
                ProcessedPaths = []
            }));
    }
}
