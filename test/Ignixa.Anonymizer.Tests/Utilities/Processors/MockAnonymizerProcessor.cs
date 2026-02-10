// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    public class MockAnonymizerProcessor : IAnonymizerProcessor
    {
        public MockAnonymizerProcessor(JsonObject settings)
        {
        }

        public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
        {
            throw new NotImplementedException();
        }
    }
}
