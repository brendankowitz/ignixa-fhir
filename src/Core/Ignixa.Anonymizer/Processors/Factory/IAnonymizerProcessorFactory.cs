// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Anonymizer.Processors;

public interface IAnonymizerProcessorFactory
{
    IAnonymizerProcessor CreateProcessor(string anonymizeMethod, JsonObject? ruleSetting = null);
}
