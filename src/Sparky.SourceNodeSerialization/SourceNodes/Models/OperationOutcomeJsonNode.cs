// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Sparky.Domain.Utility;

namespace Sparky.SourceNodeSerialization.SourceNodes.Models;

/// <summary>
/// Represents a FHIR OperationOutcome resource.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class OperationOutcomeJsonNode : ResourceJsonNode
{
    public OperationOutcomeJsonNode()
    {
        ResourceType = "OperationOutcome";
        Issue = new List<IssueComponent>();
    }

    [JsonPropertyName("issue")]
    public IList<IssueComponent> Issue { get; set; }

    /// <summary>
    /// Represents an issue detected during validation or processing.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    [SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
    public class IssueComponent
    {
        [JsonPropertyName("severity")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IssueSeverity? Severity { get; set; }

        [JsonPropertyName("code")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IssueType? Code { get; set; }

        [JsonPropertyName("diagnostics")]
        public string Diagnostics { get; set; }

        [JsonPropertyName("expression")]
        public IList<string> Expression { get; set; }

        [JsonPropertyName("details")]
        public CodeableConceptJsonNode Details { get; set; }
    }

    /// <summary>
    /// The severity of the issue (FHIR IssueSeverity value set).
    /// </summary>
    public enum IssueSeverity
    {
        [EnumLiteral("fatal")]
        Fatal,

        [EnumLiteral("error")]
        Error,

        [EnumLiteral("warning")]
        Warning,

        [EnumLiteral("information")]
        Information,
    }

    /// <summary>
    /// The type of issue (FHIR IssueType value set).
    /// </summary>
    public enum IssueType
    {
        [EnumLiteral("invalid")]
        Invalid,

        [EnumLiteral("structure")]
        Structure,

        [EnumLiteral("required")]
        Required,

        [EnumLiteral("value")]
        Value,

        [EnumLiteral("invariant")]
        Invariant,

        [EnumLiteral("security")]
        Security,

        [EnumLiteral("login")]
        Login,

        [EnumLiteral("unknown")]
        Unknown,

        [EnumLiteral("expired")]
        Expired,

        [EnumLiteral("forbidden")]
        Forbidden,

        [EnumLiteral("suppressed")]
        Suppressed,

        [EnumLiteral("processing")]
        Processing,

        [EnumLiteral("not-supported")]
        NotSupported,

        [EnumLiteral("duplicate")]
        Duplicate,

        [EnumLiteral("multiple-matches")]
        MultipleMatches,

        [EnumLiteral("not-found")]
        NotFound,

        [EnumLiteral("deleted")]
        Deleted,

        [EnumLiteral("too-long")]
        TooLong,

        [EnumLiteral("code-invalid")]
        CodeInvalid,

        [EnumLiteral("extension")]
        Extension,

        [EnumLiteral("too-costly")]
        TooCostly,

        [EnumLiteral("business-rule")]
        BusinessRule,

        [EnumLiteral("conflict")]
        Conflict,

        [EnumLiteral("transient")]
        Transient,

        [EnumLiteral("lock-error")]
        LockError,

        [EnumLiteral("no-store")]
        NoStore,

        [EnumLiteral("exception")]
        Exception,

        [EnumLiteral("timeout")]
        Timeout,

        [EnumLiteral("incomplete")]
        Incomplete,

        [EnumLiteral("throttled")]
        Throttled,

        [EnumLiteral("informational")]
        Informational,
    }
}

/// <summary>
/// Represents a FHIR CodeableConcept.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class CodeableConceptJsonNode
{
    [JsonPropertyName("coding")]
    public IList<CodingJsonNode> Coding { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

/// <summary>
/// Represents a FHIR Coding.
/// </summary>
public class CodingJsonNode
{
    [JsonPropertyName("system")]
    public string System { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("display")]
    public string Display { get; set; }

    [JsonPropertyName("userSelected")]
    public bool? UserSelected { get; set; }
}
