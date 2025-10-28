// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

public class BundleComponentSearchJsonNode : BaseJsonNode
{
    [JsonIgnore]
    public string Mode
    {
        get => MutableNode["mode"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("mode");
            }
            else
            {
                MutableNode["mode"] = value;
            }
        }
    }
}
