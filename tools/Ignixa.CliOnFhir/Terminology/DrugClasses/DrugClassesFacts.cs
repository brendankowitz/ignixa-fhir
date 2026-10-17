// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>terminology classes: the drug classes of one RxNorm code.</summary>
public sealed record DrugClassesFacts(string Rxcui, IReadOnlyList<string> ClassSources, int Total, IReadOnlyList<DrugClass> Classes, RxClassSource Source);
