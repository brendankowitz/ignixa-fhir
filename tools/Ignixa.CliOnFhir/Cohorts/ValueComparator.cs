// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>How a reading's value is compared with the criterion's value.</summary>
internal enum ValueComparator
{
    Gt,
    Ge,
    Lt,
    Le,
    Eq,
}
