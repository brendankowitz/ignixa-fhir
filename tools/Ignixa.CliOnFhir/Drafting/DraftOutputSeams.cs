// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Output;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>Points in a draft's write where a test swaps the output directory, as the other verbs' output tests do.</summary>
/// <param name="BeforeCreate">Runs between the directory's last check and the temporary file's creation.</param>
/// <param name="AfterBegin">Runs once the temporary file is open and checked, before anything is written to it.</param>
internal sealed record DraftOutputSeams(Action? BeforeCreate = null, Action<OutputFile>? AfterBegin = null);
