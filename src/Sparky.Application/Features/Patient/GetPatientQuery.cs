// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Patient;

public record GetPatientQuery(string PatientId) : IRequest<ResourceWrapper?>;
