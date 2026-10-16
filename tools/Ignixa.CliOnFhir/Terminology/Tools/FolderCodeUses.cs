// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Which of a set of codes the folder's resources carry (<see cref="FolderTerminology.Uses"/>).</summary>
/// <param name="Codes">Each code the folder uses, with the resources carrying it; codes it does not use are left out. Unordered.</param>
/// <param name="Searches">Per resource type and token search parameter, the resources carrying any of the codes; most first.</param>
public sealed record FolderCodeUses(IReadOnlyList<FolderCodeUse> Codes, IReadOnlyList<FolderCodeSearch> Searches);
