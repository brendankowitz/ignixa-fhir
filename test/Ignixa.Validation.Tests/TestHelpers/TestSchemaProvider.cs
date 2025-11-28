// <copyright file="TestSchemaProvider.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Specification.Generated;

namespace Ignixa.Validation.Tests.TestHelpers;

/// <summary>
/// Provides schema instances for validation tests.
/// Uses R4CoreSchemaProvider to provide real FHIR R4 schema metadata.
/// </summary>
public static class TestSchemaProvider
{
    // Singleton instance to avoid recreating schema for every test (performance optimization)
    private static readonly Lazy<ISchema> _r4Schema = new(() => new R4CoreSchemaProvider());

    /// <summary>
    /// Gets the FHIR R4 schema for validation tests.
    /// Returns a singleton instance for performance (schema is immutable).
    /// </summary>
#pragma warning disable CA1024 // Method provides lazy initialization, property would not convey this clearly
    public static ISchema GetR4Schema() => _r4Schema.Value;
#pragma warning restore CA1024
}
