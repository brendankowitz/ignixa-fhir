// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Models;

public class ExtensionJsonNode : BaseJsonNode
{
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "This is a POCO.")]
    [JsonIgnore]
    public string Url
    {
        get => MutableNode["url"]?.GetValue<string>();
        set => MutableNode["url"] = value;
    }
}
