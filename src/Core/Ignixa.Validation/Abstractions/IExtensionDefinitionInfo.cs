// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Describes the loaded extension definitions beyond their element tree: where each may be used
/// (<c>StructureDefinition.context</c>) and whether it is a modifier extension. Implemented by a schema
/// (<see cref="Ignixa.Abstractions.ISchema"/>) that loads extension StructureDefinitions; validation looks for it on the
/// schema it builds from, and reports the context of an extension it cannot look up as not checked.
/// </summary>
public interface IExtensionDefinitionInfo
{
    /// <summary>
    /// The definition <paramref name="url"/> names (<c>url</c> or <c>url|version</c>), or null when no extension definition
    /// with that canonical is loaded.
    /// </summary>
    /// <param name="url">The extension url, as an instance states it.</param>
    /// <returns>The definition's contexts and modifier flag, or null.</returns>
    ExtensionDefinitionInfo? GetExtensionDefinition(string url);
}
