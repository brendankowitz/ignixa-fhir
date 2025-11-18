// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Application.Features.Resource;

namespace Ignixa.Application.Operations.Features.PatientEverything;

/// <summary>
/// Query for Patient $everything operation.
/// Retrieves all resources related to a patient, including:
/// - The patient resource itself
/// - All resources in the patient compartment
/// - Referenced resources (Practitioners, Organizations, Locations, Medications)
/// </summary>
/// <param name="PatientId">The patient ID.</param>
/// <param name="Start">Optional lower bound for clinical dates (FHIR date parameter).</param>
/// <param name="End">Optional upper bound for clinical dates (FHIR date parameter).</param>
/// <param name="Since">Optional filter for resources modified after this timestamp (_since parameter).</param>
/// <param name="Types">Optional set of resource types to filter (_type parameter).</param>
/// <param name="Count">Optional pagination limit (_count parameter).</param>
public record PatientEverythingQuery(
    string PatientId,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    DateTimeOffset? Since = null,
    ISet<string>? Types = null,
    int? Count = null) : IRequest<SearchResourcesResult>;
