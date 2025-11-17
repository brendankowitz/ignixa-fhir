// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Output from importing a single terminology resource.
/// Contains import status, concept count, and error details.
/// </summary>
public record ImportTerminologyResourceOutput(
    long PackageResourceId,
    string Canonical,
    string ResourceType,
    bool Success,
    int ConceptCount,
    string? ErrorMessage);
