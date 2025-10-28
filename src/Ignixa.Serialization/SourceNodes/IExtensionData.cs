// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Serialization.SourceNodes;

public interface IExtensionData
{
    Dictionary<string, JsonElement> ExtensionData { get; }
}
