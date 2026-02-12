// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.Configuration.ProcessorSettings
{
    /// <summary>
    /// Defines how the perturbation span is calculated relative to the original value.
    /// </summary>
    public enum PerturbRangeType
    {
        Fixed,
        Proportional
    }
}