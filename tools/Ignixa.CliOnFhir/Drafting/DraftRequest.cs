// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>One draft (<c>draft_resource</c>, CLI <c>draft</c>).</summary>
/// <param name="ResourceType"><c>MedicationRequest</c>, <c>ServiceRequest</c> or <c>Observation</c>.</param>
/// <param name="Input">The typed input, inline JSON or the path of a file holding it (see the README's draft section for each type's members).</param>
/// <param name="Profile">A profile canonical (<c>url</c> or <c>url|version</c>) the draft claims and is validated against; it must be loaded, from the folder or <paramref name="Packages"/>.</param>
/// <param name="Packages">FHIR npm packages (<c>id@version</c>) whose StructureDefinitions and terminology to load first.</param>
/// <param name="Out">A directory, outside the folder, to write the draft to as <c>&lt;Type&gt;-&lt;id&gt;.json</c>.</param>
/// <param name="Overwrite">Replace that file when it exists; otherwise that is refused.</param>
/// <param name="AsOf">The call's own asOf (<see cref="Clock.AsOfClock.ForCall"/>), instead of the session's; null for the session's.</param>
/// <param name="CallMembers">Members the caller's input may also hold that are the call's, not the draft's (<c>profile</c> and <c>asOf</c> over MCP, taken out before the draft reads it), named when an unknown member is refused.</param>
/// <param name="TimeoutSeconds">How long the draft may take (default <see cref="DraftService.DefaultTimeoutSeconds"/>, at most <see cref="DraftService.MaxTimeoutSeconds"/>).</param>
public sealed record DraftRequest(
    string ResourceType,
    string Input,
    string? Profile = null,
    IReadOnlyList<string>? Packages = null,
    string? Out = null,
    bool Overwrite = false,
    int? TimeoutSeconds = null,
    string? AsOf = null,
    IReadOnlyList<string>? CallMembers = null);
