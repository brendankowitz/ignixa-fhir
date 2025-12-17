// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Handler for FHIR $member-match operation.
/// Validates input parameters and delegates to IMemberMatchStrategy for matching logic.
///
/// Per HRex specification:
/// - Input: Parameters resource with MemberPatient, CoverageToMatch, optionally CoverageToLink and Consent
/// - Output: Parameters resource with MemberIdentifier and optionally Patient reference
/// - Returns 422 Unprocessable Entity if no match or multiple matches found
/// </summary>
public class MemberMatchHandler : IRequestHandler<MemberMatchCommand, MemberMatchResult>
{
    private readonly IMemberMatchStrategy _matchStrategy;
    private readonly ILogger<MemberMatchHandler> _logger;

    public MemberMatchHandler(
        IMemberMatchStrategy matchStrategy,
        ILogger<MemberMatchHandler> logger)
    {
        _matchStrategy = matchStrategy ?? throw new ArgumentNullException(nameof(matchStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemberMatchResult> HandleAsync(
        MemberMatchCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing $member-match operation");

        // Validate input parameters
        var validationResult = ValidateInput(request);
        if (!validationResult.Success)
        {
            _logger.LogWarning("$member-match validation failed: {Error}", validationResult.ErrorMessage);
            return validationResult;
        }

        // Delegate to strategy for matching logic
        try
        {
            var result = await _matchStrategy.MatchAsync(
                request.MemberPatient,
                request.CoverageToMatch,
                request.CoverageToLink,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "$member-match completed successfully: PatientReference={PatientReference}",
                    result.PatientReference);
            }
            else
            {
                _logger.LogWarning(
                    "$member-match failed: {ErrorCode} - {ErrorMessage}",
                    result.ErrorCode,
                    result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "$member-match strategy threw an exception");
            return MemberMatchResult.InvalidInput("Internal error during member matching. Please contact the system administrator.");
        }
    }

    /// <summary>
    /// Validates the input parameters for the $member-match operation.
    /// </summary>
    private static MemberMatchResult ValidateInput(MemberMatchCommand request)
    {
        // MemberPatient is required
        if (request.MemberPatient is null)
        {
            return MemberMatchResult.InvalidInput("Required parameter 'MemberPatient' is missing.");
        }

        if (request.MemberPatient.ResourceType != KnownResourceTypes.Patient)
        {
            return MemberMatchResult.InvalidInput(
                $"Parameter 'MemberPatient' must be a Patient resource, but was '{request.MemberPatient.ResourceType}'.");
        }

        // CoverageToMatch is required
        if (request.CoverageToMatch is null)
        {
            return MemberMatchResult.InvalidInput("Required parameter 'CoverageToMatch' is missing.");
        }

        if (request.CoverageToMatch.ResourceType != KnownResourceTypes.Coverage)
        {
            return MemberMatchResult.InvalidInput(
                $"Parameter 'CoverageToMatch' must be a Coverage resource, but was '{request.CoverageToMatch.ResourceType}'.");
        }

        // CoverageToLink is optional, but if provided must be Coverage
        if (request.CoverageToLink is not null && request.CoverageToLink.ResourceType != KnownResourceTypes.Coverage)
        {
            return MemberMatchResult.InvalidInput(
                $"Parameter 'CoverageToLink' must be a Coverage resource, but was '{request.CoverageToLink.ResourceType}'.");
        }

        // Consent is optional, but if provided must be Consent
        if (request.Consent is not null && request.Consent.ResourceType != KnownResourceTypes.Consent)
        {
            return MemberMatchResult.InvalidInput(
                $"Parameter 'Consent' must be a Consent resource, but was '{request.Consent.ResourceType}'.");
        }

        // All validations passed
        return new MemberMatchResult(Success: true);
    }

    /// <summary>
    /// Builds the response Parameters resource from the match result using ParametersJsonNode.
    /// </summary>
    public static ParametersJsonNode BuildResponseParameters(MemberMatchResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException("Cannot build response parameters for failed match result.");
        }

        var parameters = new ParametersJsonNode();

        // Add MemberIdentifier parameter
        if (result.MemberIdentifier is not null)
        {
            var memberIdentifierParam = new ParameterJsonNode();
            memberIdentifierParam.Name = "MemberIdentifier";
            memberIdentifierParam.SetValue("valueIdentifier", result.MemberIdentifier.DeepClone());
            parameters.Parameter.Add(memberIdentifierParam);
        }

        // Add Patient reference parameter (optional)
        if (!string.IsNullOrEmpty(result.PatientReference))
        {
            var patientParam = new ParameterJsonNode();
            patientParam.Name = "Patient";
            patientParam.SetValue("valueReference", new JsonObject
            {
                ["reference"] = result.PatientReference
            });
            parameters.Parameter.Add(patientParam);
        }

        return parameters;
    }

    /// <summary>
    /// Builds an OperationOutcome for error responses using OperationOutcomeJsonNode.
    /// </summary>
    public static OperationOutcomeJsonNode BuildErrorOperationOutcome(MemberMatchResult result)
    {
        if (result.Success)
        {
            throw new InvalidOperationException("Cannot build error OperationOutcome for successful match result.");
        }

        var issueType = result.ErrorCode switch
        {
            "no-match" => OperationOutcomeJsonNode.IssueType.NotFound,
            "multiple-matches" => OperationOutcomeJsonNode.IssueType.MultipleMatches,
            "invalid" => OperationOutcomeJsonNode.IssueType.Invalid,
            _ => OperationOutcomeJsonNode.IssueType.Processing
        };

        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = issueType,
            Diagnostics = result.ErrorMessage ?? "Unknown error during member matching."
        });

        return outcome;
    }
}
