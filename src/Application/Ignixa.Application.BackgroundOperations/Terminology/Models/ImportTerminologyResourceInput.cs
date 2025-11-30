// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Input for importing a single terminology resource.
/// Identifies the PackageResource to import.
/// </summary>
public record ImportTerminologyResourceInput(
    int TenantId,
    long PackageResourceId);
