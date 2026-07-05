// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>FHIR Bundle.type values a search response composer can emit.</summary>
public enum ResponseBundleType
{
    Searchset,
    BatchResponse,
    TransactionResponse,
}
