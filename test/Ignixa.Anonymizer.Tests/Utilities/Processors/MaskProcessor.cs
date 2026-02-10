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
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    internal class MaskProcessor : IAnonymizerProcessor
    {
        private readonly int _maskedLength;

        public MaskProcessor(JsonObject setting)
        {
            _maskedLength = int.Parse(setting["maskedLength"]?.ToString() ?? "0");
        }

        public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
        {
            var result = new ProcessResult();
            if (node.Value == null)
            {
                return result;
            }

            var mask = new string('*', _maskedLength);
            var currentValue = node.Value.ToString() ?? string.Empty;
            var newValue = currentValue.Length > _maskedLength ? mask + currentValue[_maskedLength..] : mask;
            node.SetValue(newValue);
            return result;
        }
    }
}