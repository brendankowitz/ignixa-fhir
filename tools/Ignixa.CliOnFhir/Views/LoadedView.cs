// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>A ViewDefinition read from where the call named it, not yet validated.</summary>
/// <param name="Json">The ViewDefinition resource.</param>
/// <param name="Source"><see cref="InlineSource"/>, <see cref="FileSource"/> or <see cref="FolderSource"/>.</param>
/// <param name="FromFolder">
/// Whether its text is folder data (a ViewDefinition in the folder, or a file inside it) rather than the caller's own; a
/// problem with it is then reported without quoting the engine's message, which quotes the view.
/// </param>
/// <param name="Reference"><c>ViewDefinition/id</c> as the tools name it, for a ViewDefinition in the folder that has an id.</param>
/// <param name="Evidence">Where a ViewDefinition in the folder was read from.</param>
internal sealed record LoadedView(JsonObject Json, string Source, bool FromFolder, string? Reference, EvidenceItem? Evidence)
{
    public const string InlineSource = "inline";

    public const string FileSource = "file";

    public const string FolderSource = "folder";
}
