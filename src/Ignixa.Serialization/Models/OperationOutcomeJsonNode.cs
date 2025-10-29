// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal OperationOutcomeJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public IReadOnlyList<IssueComponent> Issue
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("issue", out var issueNode) || issueNode is not JsonArray array)
            {
                return Array.Empty<IssueComponent>();
            }

            var list = new List<IssueComponent>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<IssueComponent>(json));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Helper method to add an issue to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddIssue(IssueComponent issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        if (!MutableNode.TryGetPropertyValue("issue", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["issue"] = array;
        }

        array.Add(issue.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all issues.
    /// </summary>
    public void SetIssues(IEnumerable<IssueComponent> issues)
    {
        if (issues == null)
        {
            MutableNode.Remove("issue");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in issues)
            {
                array.Add(item.MutableNode);
            }

            MutableNode["issue"] = array;
        }
    }

    /// <summary>
    /// Represents an issue detected during validation or processing.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    [SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
    [SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
    public class IssueComponent : BaseJsonNode
    {
        // Cached wrapper for Details property
        private CodeableConceptJsonNode? _cachedDetails;

        [JsonIgnore]
        public IssueSeverity? Severity
        {
            get
            {
                var severityStr = MutableNode["severity"]?.GetValue<string>();
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
                    MutableNode["severity"] = GetIssueSeverityLiteral(value.Value);
                }
            }
        }

        [JsonIgnore]
        public IssueType? Code
        {
            get
            {
                var codeStr = MutableNode["code"]?.GetValue<string>();
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
                    MutableNode["code"] = GetIssueTypeLiteral(value.Value);
                }
            }
        }

        [JsonIgnore]
        public string Diagnostics
        {
            get => MutableNode["diagnostics"]?.GetValue<string>();
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("diagnostics");
                }
                else
                {
                    MutableNode["diagnostics"] = value;
                }
            }
        }

        [JsonIgnore]
        public IReadOnlyList<string> Expression
        {
            get
            {
                if (!MutableNode.TryGetPropertyValue("expression", out var expressionNode) || expressionNode is not JsonArray array)
                {
                    return Array.Empty<string>();
                }

                var list = new List<string>();
                foreach (var item in array)
                {
                    var value = item?.GetValue<string>();
                    if (value != null)
                    {
                        list.Add(value);
                    }
                }

                return list;
            }
        }

        /// <summary>
        /// Helper method to add an expression to the underlying JSON array.
        /// This ensures the addition is persisted to the MutableNode.
        /// </summary>
        public void AddExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
            {
                throw new ArgumentException("Expression cannot be null or empty", nameof(expression));
            }

            if (!MutableNode.TryGetPropertyValue("expression", out var node) || node is not JsonArray array)
            {
                array = new JsonArray();
                MutableNode["expression"] = array;
            }

            array.Add(JsonValue.Create(expression));
        }

        /// <summary>
        /// Helper method to replace all expressions.
        /// </summary>
        public void SetExpressions(IEnumerable<string> expressions)
        {
            if (expressions == null)
            {
                MutableNode.Remove("expression");
            }
            else
            {
                var array = new JsonArray();
                foreach (var item in expressions)
                {
                    array.Add(JsonValue.Create(item));
                }

                MutableNode["expression"] = array;
            }
        }

        [JsonIgnore]
        public CodeableConceptJsonNode Details
        {
            get
            {
                if (_cachedDetails == null)
                {
                    var internalNode = MutableNode;
                    if (internalNode.TryGetPropertyValue("details", out var detailsNode) && detailsNode is JsonObject detailsObject)
                    {
                        _cachedDetails = new CodeableConceptJsonNode { };
                        var json = detailsNode.ToJsonString();
                        _cachedDetails = JsonSerializer.Deserialize<CodeableConceptJsonNode>(json);
                    }
                }

                return _cachedDetails;
            }
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("details");
                    _cachedDetails = null;
                }
                else
                {
                    MutableNode["details"] = value.MutableNode;
                    _cachedDetails = value;
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
    [JsonIgnore]
    public IReadOnlyList<CodingJsonNode> Coding
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("coding", out var codingNode) || codingNode is not JsonArray array)
            {
                return Array.Empty<CodingJsonNode>();
            }

            var list = new List<CodingJsonNode>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<CodingJsonNode>(json));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Helper method to add a coding to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddCoding(CodingJsonNode coding)
    {
        ArgumentNullException.ThrowIfNull(coding);

        if (!MutableNode.TryGetPropertyValue("coding", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["coding"] = array;
        }

        array.Add(coding.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all codings.
    /// </summary>
    public void SetCodings(IEnumerable<CodingJsonNode> codings)
    {
        if (codings == null)
        {
            MutableNode.Remove("coding");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in codings)
            {
                array.Add(item.MutableNode);
            }

            MutableNode["coding"] = array;
        }
    }

    [JsonIgnore]
    public string Text
    {
        get => MutableNode["text"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("text");
            }
            else
            {
                MutableNode["text"] = value;
            }
        }
    }
}

/// <summary>
/// Represents a FHIR Coding.
/// </summary>
public class CodingJsonNode : BaseJsonNode
{
    [JsonIgnore]
    public string System
    {
        get => MutableNode["system"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("system");
            }
            else
            {
                MutableNode["system"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Version
    {
        get => MutableNode["version"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("version");
            }
            else
            {
                MutableNode["version"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Code
    {
        get => MutableNode["code"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("code");
            }
            else
            {
                MutableNode["code"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Display
    {
        get => MutableNode["display"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("display");
            }
            else
            {
                MutableNode["display"] = value;
            }
        }
    }

    [JsonIgnore]
    public bool? UserSelected
    {
        get => MutableNode["userSelected"]?.GetValue<bool>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("userSelected");
            }
            else
            {
                MutableNode["userSelected"] = value.Value;
            }
        }
    }
}
