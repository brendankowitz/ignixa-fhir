// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Common;

/// <summary>
/// Provides default IPS generation utilities.
/// </summary>
public static class IpsDefaults
{
    /// <summary>
    /// Creates a default Device resource to serve as the author of an IPS document.
    /// </summary>
    /// <returns>A Device resource representing the Ignixa FHIR Server.</returns>
    public static ResourceJsonNode CreateDefaultAuthor()
    {
        var deviceJson = new JsonObject
        {
            ["resourceType"] = "Device",
            ["id"] = Guid.NewGuid().ToString(),
            ["deviceName"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Ignixa FHIR Server",
                    ["type"] = "manufacturer-name"
                }
            }
        };

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(deviceJson.ToJsonString()!);
    }
}
