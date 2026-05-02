// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.DeId.Configuration
{
    /// <summary>
    /// Supported de-identification methods for FHIR resource processing.
    /// </summary>
    public enum DeIdMethod
    {
        Redact,
        DateShift,
        CryptoHash,
        Substitute,
        Encrypt,
        Perturb,
        Keep,
        Generalize
    }
}
