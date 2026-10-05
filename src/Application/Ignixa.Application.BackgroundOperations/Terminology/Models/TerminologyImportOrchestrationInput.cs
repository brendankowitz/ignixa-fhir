// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Input for terminology import orchestration.
/// Contains package metadata and list of PackageResource IDs to import.
/// A persisted dependency plan opts new jobs into dependency-first bounded scheduling.
/// Missing plans preserve the activity sequence of committed legacy histories.
/// </summary>
public record TerminologyImportOrchestrationInput(
    int TenantId,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<long> PackageResourceIds,
    [property: Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<TerminologyImportDependency>? DependencyPlan = null);
