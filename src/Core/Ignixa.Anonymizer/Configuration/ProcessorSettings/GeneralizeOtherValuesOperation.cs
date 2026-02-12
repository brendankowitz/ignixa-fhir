// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;

namespace Ignixa.Anonymizer.Configuration.ProcessorSettings
{
    /// <summary>
    /// Defines the operation to apply to values not matched by generalization cases.
    /// </summary>
    public enum GeneralizationOtherValuesOperation
    {
        Redact,
        Keep
    };
}