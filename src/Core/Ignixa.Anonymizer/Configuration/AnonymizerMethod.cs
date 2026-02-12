// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.Configuration
{
    /// <summary>
    /// Supported anonymization methods for FHIR resource processing.
    /// </summary>
    public enum AnonymizerMethod
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
