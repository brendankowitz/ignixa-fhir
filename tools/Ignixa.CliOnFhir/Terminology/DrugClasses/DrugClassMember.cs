// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>One RxNorm code of a drug class.</summary>
/// <param name="Rxcui">The RxNorm code.</param>
/// <param name="Name">Its RxNorm name (data).</param>
/// <param name="Tty">Its term type.</param>
/// <param name="Level"><c>product</c> (from the source's product relation) or <c>ingredient</c>.</param>
/// <param name="FolderResources">How many of the folder's medication resources carry the code; null when the scope is <c>all</c>.</param>
public sealed record DrugClassMember(string Rxcui, string? Name, string? Tty, string Level, int? FolderResources);
