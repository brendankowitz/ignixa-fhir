// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// A single entry from the official HL7 FHIR validator <c>manifest.json</c> test suite.
/// Only the subset of fields the baseline runner consumes is mapped; other manifest keys are ignored.
/// </summary>
public sealed class ConformanceTestCase
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    /// <summary>FHIR version under test. "4.0" == R4; absent == current R5.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    /// <summary>When false, the case is ignored by the suite.</summary>
    [JsonPropertyName("use-test")]
    public bool UseTest { get; set; } = true;

    /// <summary>IG packages the case requires (id#version). Presence excludes a case from the clean-base slice.</summary>
    [JsonPropertyName("packages")]
    public List<string>? Packages { get; set; }

    /// <summary>Extra resources to load before validating. Presence excludes a case from the clean-base slice.</summary>
    [JsonPropertyName("supporting")]
    public List<string>? Supporting { get; set; }

    /// <summary>Alias for <see cref="Supporting"/>. Presence excludes a case from the clean-base slice.</summary>
    [JsonPropertyName("profiles")]
    public List<string>? Profiles { get; set; }

    /// <summary>Explicit profile to validate against. Presence excludes a case from the clean-base slice.</summary>
    [JsonPropertyName("profile")]
    public JsonElement? Profile { get; set; }

    /// <summary>Logical-model configuration for cases that cannot be auto-recognized. Presence excludes a case from the clean-base slice.</summary>
    [JsonPropertyName("logical")]
    public JsonElement? Logical { get; set; }

    /// <summary>
    /// Expected outcome from the HL7 Java reference validator: either an inline object
    /// (<c>errorCount</c>, or a nested <c>outcome</c> OperationOutcome) or a string path resolved
    /// under <c>validator/outcomes/</c>.
    /// </summary>
    [JsonPropertyName("java")]
    public JsonElement? Java { get; set; }

    /// <summary>When true, JSON <c>//</c> comments in the input are tolerated (JSON5 module).</summary>
    [JsonPropertyName("allow-comments")]
    public bool AllowComments { get; set; }

    /// <summary>When true, security-checks mode is enabled (embedded HTML in strings is rejected).</summary>
    [JsonPropertyName("security-checks")]
    public bool SecurityChecks { get; set; }

    /// <summary>When true, embedded HTML in markdown is flagged.</summary>
    [JsonPropertyName("noHtmlInMarkdown")]
    public bool NoHtmlInMarkdown { get; set; }

    /// <summary>
    /// Spec-mode toggle for example URLs. When explicitly <c>false</c>, example.org / acme.com URLs are
    /// rejected; when true (or absent) they are permitted. Nullable to distinguish "not set" from false.
    /// </summary>
    [JsonPropertyName("examples")]
    public bool? Examples { get; set; }

    /// <summary>Contained-resource validation mode: <c>IGNORE</c> skips validating contained resources.</summary>
    [JsonPropertyName("validateContains")]
    public string? ValidateContains { get; set; }
}
