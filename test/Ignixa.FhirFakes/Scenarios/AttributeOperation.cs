// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Operations for attribute manipulation.
/// </summary>
public enum AttributeOperation
{
    /// <summary>Set attribute value</summary>
    Set,

    /// <summary>Increment numeric attribute</summary>
    Increment,

    /// <summary>Decrement numeric attribute</summary>
    Decrement
}
