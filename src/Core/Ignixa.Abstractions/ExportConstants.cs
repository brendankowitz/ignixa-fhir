// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Constants;

/// <summary>
/// Constants for FHIR bulk export operations.
/// </summary>
public static class ExportConstants
{
    /// <summary>
    /// MIME type for FHIR NDJSON format (newline-delimited JSON).
    /// This is the default and most common format for FHIR bulk data export.
    /// </summary>
    public const string MediaTypeNdjson = "application/fhir+ndjson";

    /// <summary>
    /// MIME type for Apache Parquet format.
    /// Used for analytics-friendly columnar export with SQL-on-FHIR ViewDefinition support.
    /// </summary>
    public const string MediaTypeParquet = "application/vnd.apache.parquet";
}
