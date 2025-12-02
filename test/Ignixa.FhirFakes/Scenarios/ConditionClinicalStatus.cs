// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// FHIR Condition clinical status codes.
/// </summary>
public static class ConditionClinicalStatus
{
    /// <summary>The subject is currently experiencing the condition</summary>
    public const string Active = "active";

    /// <summary>The subject is no longer experiencing the condition</summary>
    public const string Resolved = "resolved";

    /// <summary>The subject is not presently experiencing the condition</summary>
    public const string Inactive = "inactive";

    /// <summary>The condition is temporarily controlled but may return</summary>
    public const string Remission = "remission";

    /// <summary>The condition was entered in error</summary>
    public const string EnteredInError = "entered-in-error";
}
