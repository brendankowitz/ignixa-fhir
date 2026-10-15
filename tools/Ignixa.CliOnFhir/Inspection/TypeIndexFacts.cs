// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Size and build time of one resource type's index.</summary>
public sealed record TypeIndexFacts(string ResourceType, int ResourceCount, long IndexEntries, long BuildMilliseconds);
