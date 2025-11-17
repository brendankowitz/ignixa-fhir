// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Events.Terminology;

/// <summary>
/// Event published when terminology import should be triggered.
/// Contains PackageResourceIds for CodeSystem/ValueSet/ConceptMap resources that need importing.
/// </summary>
public record TerminologyImportTriggeredEvent(
    int TenantId,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<long> PackageResourceIds) : ITerminologyImportTriggered;
