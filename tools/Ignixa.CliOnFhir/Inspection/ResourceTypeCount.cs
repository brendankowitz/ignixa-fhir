// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Number of indexed resources of one type.</summary>
public sealed record ResourceTypeCount(string ResourceType, int Count);
