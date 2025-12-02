// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Comparison operators for conditions and guards.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Equal to (==)</summary>
    EqualTo,

    /// <summary>Not equal to (!=)</summary>
    NotEqualTo,

    /// <summary>Greater than (>)</summary>
    GreaterThan,

    /// <summary>Greater than or equal to (>=)</summary>
    GreaterThanOrEqualTo,

    /// <summary>Less than (<)</summary>
    LessThan,

    /// <summary>Less than or equal to (<=)</summary>
    LessThanOrEqualTo
}
