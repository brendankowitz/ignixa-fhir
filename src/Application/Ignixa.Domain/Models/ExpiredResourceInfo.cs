// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Metadata for an expired resource from the TTL table.
/// Used by TTL cleanup background job to identify resources that need hard deletion.
/// </summary>
public record ExpiredResourceInfo(
    short ResourceTypeId,
    string ResourceId,
    DateTimeOffset ExpiresAt,
    string ResourceType);
