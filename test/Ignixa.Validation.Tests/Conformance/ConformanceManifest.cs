// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>Root of the official FHIR validator <c>manifest.json</c>.</summary>
public sealed class ConformanceManifest
{
    [JsonPropertyName("test-cases")]
    public List<ConformanceTestCase> TestCases { get; set; } = [];
}
