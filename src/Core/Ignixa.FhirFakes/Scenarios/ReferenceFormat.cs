// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Specifies the format for resource references in generated scenarios.
/// </summary>
public enum ReferenceFormat
{
    /// <summary>
    /// Use urn:uuid format (e.g., "urn:uuid:a1b2c3d4-e5f6-7890-abcd-1234567890ab").
    /// Suitable for transaction bundles with client-assigned IDs.
    /// This is the default format.
    /// </summary>
    UrnUuid,

    /// <summary>
    /// Use resolved format (e.g., "Patient/a1b2c3d4-e5f6-7890-abcd-1234567890ab").
    /// Suitable for batch bundles and when resources are already created on the server.
    /// </summary>
    Resolved
}
