// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>terminology class-search: drug classes whose names match the text, best first.</summary>
/// <param name="SearchedAs">The words matched, after a fixed alias (statin, SSRI, ...) was applied; null when none was.</param>
public sealed record DrugClassSearchFacts(IReadOnlyList<string> ClassSources, string? SearchedAs, int Total, int Offset, int Returned, IReadOnlyList<DrugClass> Classes, RxClassSource Source);
