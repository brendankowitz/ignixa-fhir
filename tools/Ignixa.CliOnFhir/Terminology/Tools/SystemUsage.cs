// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>How the codes of one system used in the folder resolve against the loaded terminology.</summary>
/// <param name="System">The system, as the folder writes it (folder data).</param>
/// <param name="Status"><c>loaded</c>, or <c>system-not-loaded</c> (including a content-not-present stub).</param>
/// <param name="Version">The loaded version codes were resolved against.</param>
/// <param name="Source">The package (<c>id@version</c>) or <c>folder</c> it came from.</param>
/// <param name="ContentComplete">Whether the loaded content is complete; when false, <paramref name="Unknown"/> codes may still exist.</param>
/// <param name="DistinctCodes">Distinct codes of the system in the folder.</param>
/// <param name="Resolved">Codes the loaded version defines (active or inactive).</param>
/// <param name="Inactive">Resolved codes that are inactive.</param>
/// <param name="Unknown">Codes the loaded version does not define; null when the system is not loaded.</param>
public sealed record SystemUsage(string System, string Status, string? Version, string? Source, bool? ContentComplete, int DistinctCodes, int? Resolved, int? Inactive, int? Unknown);
