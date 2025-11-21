// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Actions the visitor can take for a property.
/// </summary>
public enum PropertyAction
{
    /// <summary>
    /// Include the property unchanged.
    /// </summary>
    Include,

    /// <summary>
    /// Skip the property (exclude from output).
    /// </summary>
    Skip,

    /// <summary>
    /// Mutate the property value.
    /// </summary>
    Mutate,

    /// <summary>
    /// Inject a new property (for missing elements).
    /// </summary>
    Inject
}
