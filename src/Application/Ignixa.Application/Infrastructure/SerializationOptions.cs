// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

public class SerializationOptions
{
    public const string SectionName = "Serialization";

    /// <summary>
    /// Reorders each resource's properties into StructureDefinition element order before it is stored,
    /// so reads return spec-ordered JSON. Off by default: resources are stored in the order the client sent.
    /// </summary>
    public bool OrderElementsOnWrite { get; set; }
}
