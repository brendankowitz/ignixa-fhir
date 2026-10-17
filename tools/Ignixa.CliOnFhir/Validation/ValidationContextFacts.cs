// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>What the resources were validated against.</summary>
/// <param name="Depth"><c>full</c> (the default) or <c>spec</c>.</param>
/// <param name="Profile">The one profile requested, when one was; otherwise each resource's loaded <c>meta.profile</c> applied.</param>
/// <param name="Packages">Packages whose StructureDefinitions and terminology were loaded for this call (<c>id@version</c>).</param>
/// <param name="ProfilesLoaded">StructureDefinitions available beyond the core ones (from the folder and those packages).</param>
/// <param name="TerminologySources">The terminology sources binding checks used (<c>folder</c>, <c>id@version</c>), in precedence order.</param>
/// <param name="TxServer">Whether bindings the local terminology could not decide were asked of the <c>--tx-server</c>.</param>
/// <param name="DisplaysChecked">Always false: coding displays are not checked.</param>
/// <param name="ChecksNotRun">
/// The checks that did not run at this depth: at <c>spec</c> invariants, slicing, extensible bindings and extension
/// definitions (so no resource is reported valid, see <c>depth-spec</c>); at every depth displays (an extension whose
/// definition is not loaded, or whose context cannot be checked, is reported per resource, see
/// <c>extension-definition-unchecked</c> and <c>extension-context-unchecked</c>); and
/// the references whose target type could not be checked, counted (they cannot change a verdict).
/// </param>
public sealed record ValidationContextFacts(
    string Depth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Profile,
    IReadOnlyList<string> Packages,
    int ProfilesLoaded,
    IReadOnlyList<string> TerminologySources,
    bool TxServer,
    bool DisplaysChecked,
    IReadOnlyList<string> ChecksNotRun);
