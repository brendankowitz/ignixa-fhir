// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>An RxNorm concept as RxClass names it: its RxCUI, name and term type (IN ingredient, SCD/SBD/GPCK/BPCK products, ...). The name is RxNorm data.</summary>
public sealed record DrugConcept(string Rxcui, string? Name, string? Tty);
