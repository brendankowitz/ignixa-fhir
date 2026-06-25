/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * xunit collection definition binding the SQL on FHIR report collector fixture.
 */

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Collection definition that shares a single <see cref="SqlOnFhirReportCollector"/> across the
/// conformance runner so the report is written once after the last case.
/// </summary>
[CollectionDefinition("SqlOnFhirReport")]
#pragma warning disable CA1711 // xunit collection-definition naming convention requires the 'Collection' suffix.
public class SqlOnFhirReportCollection : ICollectionFixture<SqlOnFhirReportCollector>
#pragma warning restore CA1711
{
}
