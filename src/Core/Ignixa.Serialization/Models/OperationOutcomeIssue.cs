// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;

namespace Ignixa.Models;

public partial class OperationOutcomeIssue
{
    /// <summary>
    /// Gets/sets <c>severity</c> via a version-agnostic enum. <c>severity</c> is a version-tagged enum on
    /// the R4/R5 subclasses (not the shared base) because R5 adds a "success" literal to the
    /// <c>issue-severity</c> value set -- but every real caller in this codebase only ever reads/writes one
    /// of the 4 literals common to both versions, so this covers every real usage without needing a
    /// version-specific type. Distinctly named (not <c>Severity</c>) to avoid the <c>new</c>-modifier
    /// shadowing hazard <see cref="Extension"/>'s <c>ValueString</c> merge established: a same-named
    /// hand-written member on the shared base would silently hide the version-correct generated property
    /// for any caller holding a base-typed reference.
    /// </summary>
    public IssueSeverityCode? SeverityCode
    {
        get => EnumUtility.ParseLiteral<IssueSeverityCode>(GetProperty<string>("severity"));
        set => SetProperty("severity", value?.GetLiteral());
    }

    /// <summary>
    /// Gets/sets <c>code</c> via a version-agnostic enum. Same rationale as <see cref="SeverityCode"/>:
    /// R5 adds two literals ("limited-filter", "success") to the <c>issue-type</c> value set, but every
    /// real caller only uses one of the 31 literals common to both versions. Named <see cref="IssueTypeCommon"/>,
    /// not <c>IssueType</c>, to avoid colliding with the generated per-version
    /// <see cref="Ignixa.Models.R4.IssueType"/>/<see cref="Ignixa.Models.R5.IssueType"/> top-level types: an
    /// unqualified <c>IssueType</c> reference inside those generated subclasses would otherwise resolve to
    /// this nested type (inherited-member lookup wins over namespace lookup), silently retyping their
    /// generated <c>Code</c> property to the narrower common subset instead of the real per-version enum.
    /// </summary>
    public IssueTypeCommon? IssueTypeCode
    {
        get => EnumUtility.ParseLiteral<IssueTypeCommon>(GetProperty<string>("code"));
        set => SetProperty("code", value?.GetLiteral());
    }

    /// <summary>
    /// The severity of the issue (FHIR IssueSeverity value set, R4/R5-common subset).
    /// </summary>
    public enum IssueSeverityCode
    {
        [EnumLiteral("fatal", "http://hl7.org/fhir/issue-severity")]
        Fatal,

        [EnumLiteral("error", "http://hl7.org/fhir/issue-severity")]
        Error,

        [EnumLiteral("warning", "http://hl7.org/fhir/issue-severity")]
        Warning,

        [EnumLiteral("information", "http://hl7.org/fhir/issue-severity")]
        Information,
    }

    /// <summary>
    /// The type of issue (FHIR IssueType value set, R4/R5-common subset). Named <c>IssueTypeCommon</c>
    /// rather than <c>IssueType</c> to avoid shadowing the generated per-version
    /// <see cref="Ignixa.Models.R4.IssueType"/>/<see cref="Ignixa.Models.R5.IssueType"/> types -- see
    /// <see cref="IssueTypeCode"/>.
    /// </summary>
    public enum IssueTypeCommon
    {
        [EnumLiteral("invalid", "http://hl7.org/fhir/issue-type")]
        Invalid,

        [EnumLiteral("structure", "http://hl7.org/fhir/issue-type")]
        Structure,

        [EnumLiteral("required", "http://hl7.org/fhir/issue-type")]
        Required,

        [EnumLiteral("value", "http://hl7.org/fhir/issue-type")]
        Value,

        [EnumLiteral("invariant", "http://hl7.org/fhir/issue-type")]
        Invariant,

        [EnumLiteral("security", "http://hl7.org/fhir/issue-type")]
        Security,

        [EnumLiteral("login", "http://hl7.org/fhir/issue-type")]
        Login,

        [EnumLiteral("unknown", "http://hl7.org/fhir/issue-type")]
        Unknown,

        [EnumLiteral("expired", "http://hl7.org/fhir/issue-type")]
        Expired,

        [EnumLiteral("forbidden", "http://hl7.org/fhir/issue-type")]
        Forbidden,

        [EnumLiteral("suppressed", "http://hl7.org/fhir/issue-type")]
        Suppressed,

        [EnumLiteral("processing", "http://hl7.org/fhir/issue-type")]
        Processing,

        [EnumLiteral("not-supported", "http://hl7.org/fhir/issue-type")]
        NotSupported,

        [EnumLiteral("duplicate", "http://hl7.org/fhir/issue-type")]
        Duplicate,

        [EnumLiteral("multiple-matches", "http://hl7.org/fhir/issue-type")]
        MultipleMatches,

        [EnumLiteral("not-found", "http://hl7.org/fhir/issue-type")]
        NotFound,

        [EnumLiteral("deleted", "http://hl7.org/fhir/issue-type")]
        Deleted,

        [EnumLiteral("too-long", "http://hl7.org/fhir/issue-type")]
        TooLong,

        [EnumLiteral("code-invalid", "http://hl7.org/fhir/issue-type")]
        CodeInvalid,

        [EnumLiteral("extension", "http://hl7.org/fhir/issue-type")]
        Extension,

        [EnumLiteral("too-costly", "http://hl7.org/fhir/issue-type")]
        TooCostly,

        [EnumLiteral("business-rule", "http://hl7.org/fhir/issue-type")]
        BusinessRule,

        [EnumLiteral("conflict", "http://hl7.org/fhir/issue-type")]
        Conflict,

        [EnumLiteral("transient", "http://hl7.org/fhir/issue-type")]
        Transient,

        [EnumLiteral("lock-error", "http://hl7.org/fhir/issue-type")]
        LockError,

        [EnumLiteral("no-store", "http://hl7.org/fhir/issue-type")]
        NoStore,

        [EnumLiteral("exception", "http://hl7.org/fhir/issue-type")]
        Exception,

        [EnumLiteral("timeout", "http://hl7.org/fhir/issue-type")]
        Timeout,

        [EnumLiteral("incomplete", "http://hl7.org/fhir/issue-type")]
        Incomplete,

        [EnumLiteral("throttled", "http://hl7.org/fhir/issue-type")]
        Throttled,

        [EnumLiteral("informational", "http://hl7.org/fhir/issue-type")]
        Informational,
    }
}
