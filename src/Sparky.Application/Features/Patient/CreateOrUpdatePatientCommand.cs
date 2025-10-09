// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Medino;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Patient;

public record CreateOrUpdatePatientCommand(
    string PatientId,
    ISourceNode Resource) : IRequest<ResourceKey>;
