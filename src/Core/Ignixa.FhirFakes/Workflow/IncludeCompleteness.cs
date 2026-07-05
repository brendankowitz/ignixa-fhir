// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// How completely a composed bundle includes resources referenced by its primary matches.
/// <see cref="Complete"/> includes every non-matching resource in the graph once; <see cref="Missing"/>
/// omits them, so a consumer sees a reference it cannot resolve from the bundle alone.
/// </summary>
public enum IncludeCompleteness
{
    Complete,
    Missing,
}
