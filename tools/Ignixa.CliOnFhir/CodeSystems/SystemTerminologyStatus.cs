// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>How a code system's codes resolve against terminology already loaded in the session.</summary>
/// <param name="Loaded"><c>local</c> (a loaded CodeSystem provides its concepts), <c>not-loaded</c>, or <c>tx-server-available</c> (not loaded, but a --tx-server is configured).</param>
/// <param name="Package">The package (<c>id@version</c>) or <c>folder</c> that answers.</param>
/// <param name="Content">That CodeSystem's content mode.</param>
/// <param name="Version">Its version.</param>
/// <param name="Resolved">Distinct codes it defines.</param>
/// <param name="Inactive">Resolved codes that are inactive.</param>
/// <param name="Unknown">Distinct codes it does not define (authoritative only when <paramref name="Content"/> is complete).</param>
public sealed record SystemTerminologyStatus(string Loaded, string? Package, string? Content, string? Version, int? Resolved, int? Inactive, int? Unknown);
