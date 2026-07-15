// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Models;

public partial class Reference
{
    /// <summary>
    /// Creates a Reference from a resource type and id.
    /// </summary>
    public static Reference FromResourceTypeAndId(string resourceType, string id)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);

        return new Reference
        {
            Reference2 = $"{resourceType}/{id}"
        };
    }
}
