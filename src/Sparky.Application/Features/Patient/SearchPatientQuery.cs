// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Sparky.Search.Models;

namespace Sparky.Application.Features.Patient;

/// <summary>
/// Query to search for Patient resources.
/// </summary>
/// <param name="SearchOptions">The search options parsed from query parameters.</param>
public record SearchPatientQuery(SearchOptions SearchOptions) : IRequest<SearchPatientResult>;
