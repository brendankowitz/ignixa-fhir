// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

public class SerializationOptions
{
    public const string SectionName = "Serialization";

    /// <summary>
    /// Reorders each resource's properties into StructureDefinition element order before it is stored.
    /// Applies only to writes made while enabled; resources already stored are not rewritten.
    /// Off by default: the client's property order is kept (a missing <c>meta</c> is appended).
    /// </summary>
    public bool OrderElementsOnWrite { get; set; }
}
