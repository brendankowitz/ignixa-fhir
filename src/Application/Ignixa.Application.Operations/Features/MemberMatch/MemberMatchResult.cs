// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Result of a FHIR $member-match operation.
/// Contains the matched member identifier and optional patient reference.
/// </summary>
/// <param name="Success">Whether a unique match was found.</param>
/// <param name="MemberIdentifier">The matched member's unique identifier (null if no match).</param>
/// <param name="PatientReference">Optional reference to matched Patient resource on target system.</param>
/// <param name="ErrorCode">Error code if matching failed (e.g., "no-match", "multiple-matches").</param>
/// <param name="ErrorMessage">Detailed error message if matching failed.</param>
public record MemberMatchResult(
    bool Success,
    JsonNode? MemberIdentifier = null,
    string? PatientReference = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Creates a successful match result.
    /// </summary>
    /// <param name="memberIdentifier">The matched member's identifier as a FHIR Identifier element.</param>
    /// <param name="patientReference">Optional reference to the matched Patient resource.</param>
    public static MemberMatchResult Matched(JsonNode memberIdentifier, string? patientReference = null)
        => new(Success: true, MemberIdentifier: memberIdentifier, PatientReference: patientReference);

    /// <summary>
    /// Creates a no-match result (HTTP 422).
    /// </summary>
    /// <param name="message">Detailed message explaining why no match was found.</param>
    public static MemberMatchResult NoMatch(string? message = null)
        => new(
            Success: false,
            ErrorCode: "no-match",
            ErrorMessage: message ?? "No matching member found with the provided demographics and coverage information.");

    /// <summary>
    /// Creates a multiple-matches result (HTTP 422).
    /// </summary>
    /// <param name="message">Detailed message explaining the multiple matches situation.</param>
    public static MemberMatchResult MultipleMatches(string? message = null)
        => new(
            Success: false,
            ErrorCode: "multiple-matches",
            ErrorMessage: message ?? "Multiple members matched the provided demographics and coverage information. Unable to determine a unique match.");

    /// <summary>
    /// Creates an invalid input result (HTTP 400).
    /// </summary>
    /// <param name="message">Detailed message explaining the validation error.</param>
    public static MemberMatchResult InvalidInput(string message)
        => new(
            Success: false,
            ErrorCode: "invalid",
            ErrorMessage: message);
}
