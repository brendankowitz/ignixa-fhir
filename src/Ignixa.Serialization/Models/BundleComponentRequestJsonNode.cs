// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1056", Justification = "POCO style model")]
public class BundleComponentRequestJsonNode : BaseJsonNode
{
    [JsonIgnore]
    public string Method
    {
        get => MutableNode["method"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("method");
            }
            else
            {
                MutableNode["method"] = value;
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
