// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1056", Justification = "POCO style model")]
public class BundleLinkJsonNode : BaseJsonNode
{
    [JsonIgnore]
    public string Relation
    {
        get => MutableNode["relation"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("relation");
            }
            else
            {
                MutableNode["relation"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Url
    {
        get => MutableNode["url"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("url");
            }
            else
            {
                MutableNode["url"] = value;
            }
        }
    }
}
