// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

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
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public OperationOutcomeJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public MutableJsonList<IssueComponent> Issue => GetListProperty<IssueComponent>("issue");

    /// <summary>
    /// Represents an issue detected during validation or processing.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    [SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
    [SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
    public class IssueComponent : BaseJsonNode
    {
        public IssueComponent()
            : this(new JsonObject(), null)
        {
        }

        public IssueComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public IssueSeverity? Severity
        {
            get
            {
                var severityStr = GetProperty<string>("severity");
                return severityStr != null ? ParseIssueSeverity(severityStr) : null;
            }
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("severity");
                }
                else
                {
                    SetProperty("severity", GetIssueSeverityLiteral(value.Value));
                }
            }
        }

        [JsonIgnore]
        public IssueType? Code
        {
            get
            {
                var codeStr = GetProperty<string>("code");
                return codeStr != null ? ParseIssueType(codeStr) : null;
            }
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("code");
                }
                else
                {
                    SetProperty("code", GetIssueTypeLiteral(value.Value));
                }
            }
        }

        [JsonIgnore]
        public string Diagnostics
        {
            get => GetProperty<string>("diagnostics");
            set => SetProperty("diagnostics", value);
        }

        [JsonIgnore]
        public MutablePrimitiveList<string> Expression => GetPrimitiveListProperty<string>("expression");

        [JsonIgnore]
        public CodeableConceptJsonNode Details
        {
            get => GetComplexProperty<CodeableConceptJsonNode>("details");
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("details");
                }
                else
                {
                    MutableNode["details"] = value.MutableNode;
                }
            }
        }

        private static IssueSeverity? ParseIssueSeverity(string value)
        {
            return value switch
            {
                "fatal" => IssueSeverity.Fatal,
                "error" => IssueSeverity.Error,
                "warning" => IssueSeverity.Warning,
                "information" => IssueSeverity.Information,
                _ => null,
            };
        }

        private static string GetIssueSeverityLiteral(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Fatal => "fatal",
                IssueSeverity.Error => "error",
                IssueSeverity.Warning => "warning",
                IssueSeverity.Information => "information",
                _ => throw new ArgumentOutOfRangeException(nameof(severity)),
            };
        }

        private static IssueType? ParseIssueType(string value)
        {
            return value switch
            {
                "invalid" => IssueType.Invalid,
                "structure" => IssueType.Structure,
                "required" => IssueType.Required,
                "value" => IssueType.Value,
                "invariant" => IssueType.Invariant,
                "security" => IssueType.Security,
                "login" => IssueType.Login,
                "unknown" => IssueType.Unknown,
                "expired" => IssueType.Expired,
                "forbidden" => IssueType.Forbidden,
                "suppressed" => IssueType.Suppressed,
                "processing" => IssueType.Processing,
                "not-supported" => IssueType.NotSupported,
                "duplicate" => IssueType.Duplicate,
                "multiple-matches" => IssueType.MultipleMatches,
                "not-found" => IssueType.NotFound,
                "deleted" => IssueType.Deleted,
                "too-long" => IssueType.TooLong,
                "code-invalid" => IssueType.CodeInvalid,
                "extension" => IssueType.Extension,
                "too-costly" => IssueType.TooCostly,
                "business-rule" => IssueType.BusinessRule,
                "conflict" => IssueType.Conflict,
                "transient" => IssueType.Transient,
                "lock-error" => IssueType.LockError,
                "no-store" => IssueType.NoStore,
                "exception" => IssueType.Exception,
                "timeout" => IssueType.Timeout,
                "incomplete" => IssueType.Incomplete,
                "throttled" => IssueType.Throttled,
                "informational" => IssueType.Informational,
                _ => null,
            };
        }

        private static string GetIssueTypeLiteral(IssueType type)
        {
            return type switch
            {
                IssueType.Invalid => "invalid",
                IssueType.Structure => "structure",
                IssueType.Required => "required",
                IssueType.Value => "value",
                IssueType.Invariant => "invariant",
                IssueType.Security => "security",
                IssueType.Login => "login",
                IssueType.Unknown => "unknown",
                IssueType.Expired => "expired",
                IssueType.Forbidden => "forbidden",
                IssueType.Suppressed => "suppressed",
                IssueType.Processing => "processing",
                IssueType.NotSupported => "not-supported",
                IssueType.Duplicate => "duplicate",
                IssueType.MultipleMatches => "multiple-matches",
                IssueType.NotFound => "not-found",
                IssueType.Deleted => "deleted",
                IssueType.TooLong => "too-long",
                IssueType.CodeInvalid => "code-invalid",
                IssueType.Extension => "extension",
                IssueType.TooCostly => "too-costly",
                IssueType.BusinessRule => "business-rule",
                IssueType.Conflict => "conflict",
                IssueType.Transient => "transient",
                IssueType.LockError => "lock-error",
                IssueType.NoStore => "no-store",
                IssueType.Exception => "exception",
                IssueType.Timeout => "timeout",
                IssueType.Incomplete => "incomplete",
                IssueType.Throttled => "throttled",
                IssueType.Informational => "informational",
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }
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
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class CodeableConceptJsonNode : BaseJsonNode
{
    public CodeableConceptJsonNode()
        : this(new JsonObject(), null)
    {
    }

    public CodeableConceptJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public MutableJsonList<CodingJsonNode> Coding => GetListProperty<CodingJsonNode>("coding");

    [JsonIgnore]
    public string Text
    {
        get => GetProperty<string>("text");
        set => SetProperty("text", value);
    }
}

/// <summary>
/// Represents a FHIR Coding.
/// </summary>
public class CodingJsonNode : BaseJsonNode
{
    public CodingJsonNode()
        : this(new JsonObject(), null)
    {
    }

    public CodingJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string System
    {
        get => GetProperty<string>("system");
        set => SetProperty("system", value);
    }

    [JsonIgnore]
    public string Version
    {
        get => GetProperty<string>("version");
        set => SetProperty("version", value);
    }

    [JsonIgnore]
    public string Code
    {
        get => GetProperty<string>("code");
        set => SetProperty("code", value);
    }

    [JsonIgnore]
    public string Display
    {
        get => GetProperty<string>("display");
        set => SetProperty("display", value);
    }

    [JsonIgnore]
    public bool? UserSelected
    {
        get => GetProperty<bool?>("userSelected");
        set => SetProperty("userSelected", value);
    }
}
