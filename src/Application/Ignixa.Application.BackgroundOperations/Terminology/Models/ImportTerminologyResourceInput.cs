// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Input for importing a single terminology resource.
/// Identifies the PackageResource to import. DependencyFailure records a blocked new-plan resource as
/// failed without invoking the importer; omitted values retain the legacy serialized input shape.
/// </summary>
public record ImportTerminologyResourceInput(
    int TenantId,
    long PackageResourceId,
    [property: Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? DependencyFailure = null);
