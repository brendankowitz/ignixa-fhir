// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Text.Json.Nodes;
using Ignixa.Application.Operations.Features.MemberMatch;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.MemberMatch;

/// <summary>
/// Unit tests for MemberMatchHandler.
/// Tests the FHIR $member-match operation handler logic, including parameter validation
/// and response generation.
/// </summary>
public class MemberMatchHandlerTests
{
    private readonly IMemberMatchStrategy _matchStrategy;
    private readonly MemberMatchHandler _handler;

    public MemberMatchHandlerTests()
    {
        _matchStrategy = Substitute.For<IMemberMatchStrategy>();
        _handler = new MemberMatchHandler(
            _matchStrategy,
            NullLogger<MemberMatchHandler>.Instance);
    }

    #region Input Validation Tests

    [Fact]
    public async Task GivenNullMemberPatient_WhenProcessing_ThenReturnsInvalidInputError()
    {
        // Arrange
        var coverageJson = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;
        var coverageNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(coverageJson)), CancellationToken.None);

        var command = new MemberMatchCommand(
            MemberPatient: null,
            CoverageToMatch: coverageNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("invalid");
        result.ErrorMessage.ShouldContain("MemberPatient");
    }

    [Fact]
    public async Task GivenNullCoverageToMatch_WhenProcessing_ThenReturnsInvalidInputError()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "identifier": [
                {
                    "system": "http://example.org/members",
                    "value": "MEM12345"
                }
            ]
        }
        """;
        var patientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);

        var command = new MemberMatchCommand(
            MemberPatient: patientNode,
            CoverageToMatch: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("invalid");
        result.ErrorMessage.ShouldContain("CoverageToMatch");
    }

    [Fact]
    public async Task GivenWrongResourceTypeForMemberPatient_WhenProcessing_ThenReturnsInvalidInputError()
    {
        // Arrange - MemberPatient is Observation instead of Patient
        var observationJson = """
        {
            "resourceType": "Observation",
            "id": "obs-123"
        }
        """;
        var observationNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(observationJson)), CancellationToken.None);

        var coverageJson = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;
        var coverageNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(coverageJson)), CancellationToken.None);

        var command = new MemberMatchCommand(
            MemberPatient: observationNode,
            CoverageToMatch: coverageNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("invalid");
        result.ErrorMessage.ShouldContain("Patient");
    }

    [Fact]
    public async Task GivenWrongResourceTypeForCoverageToMatch_WhenProcessing_ThenReturnsInvalidInputError()
    {
        // Arrange - CoverageToMatch is Patient instead of Coverage
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123"
        }
        """;
        var patientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);

        var anotherPatientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-456"
        }
        """;
        var anotherPatientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(anotherPatientJson)), CancellationToken.None);

        var command = new MemberMatchCommand(
            MemberPatient: patientNode,
            CoverageToMatch: anotherPatientNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("invalid");
        result.ErrorMessage.ShouldContain("Coverage");
    }

    #endregion

    #region Successful Match Tests

    [Fact]
    public async Task GivenValidInputWithMatch_WhenProcessing_ThenReturnsSuccessWithIdentifier()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "identifier": [
                {
                    "system": "http://example.org/members",
                    "value": "MEM12345"
                }
            ]
        }
        """;
        var patientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);

        var coverageJson = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;
        var coverageNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(coverageJson)), CancellationToken.None);

        var memberIdentifier = new JsonObject
        {
            ["system"] = "http://example.org/members",
            ["value"] = "MEM12345"
        };

        _matchStrategy.MatchAsync(Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<CancellationToken>())
            .Returns(MemberMatchResult.Matched(memberIdentifier, "Patient/matched-patient-123"));

        var command = new MemberMatchCommand(
            MemberPatient: patientNode,
            CoverageToMatch: coverageNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeTrue();
        result.MemberIdentifier.ShouldNotBeNull();
        result.PatientReference.ShouldBe("Patient/matched-patient-123");
    }

    #endregion

    #region No Match Tests

    [Fact]
    public async Task GivenValidInputWithNoMatch_WhenProcessing_ThenReturnsNoMatchError()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "identifier": [
                {
                    "system": "http://example.org/members",
                    "value": "UNKNOWN_MEMBER"
                }
            ]
        }
        """;
        var patientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);

        var coverageJson = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;
        var coverageNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(coverageJson)), CancellationToken.None);

        _matchStrategy.MatchAsync(Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<CancellationToken>())
            .Returns(MemberMatchResult.NoMatch());

        var command = new MemberMatchCommand(
            MemberPatient: patientNode,
            CoverageToMatch: coverageNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("no-match");
    }

    #endregion

    #region Multiple Matches Tests

    [Fact]
    public async Task GivenValidInputWithMultipleMatches_WhenProcessing_ThenReturnsMultipleMatchesError()
    {
        // Arrange
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "identifier": [
                {
                    "system": "http://example.org/members",
                    "value": "DUPLICATE_MEMBER"
                }
            ]
        }
        """;
        var patientNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(patientJson)), CancellationToken.None);

        var coverageJson = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;
        var coverageNode = await JsonSourceNodeFactory.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(coverageJson)), CancellationToken.None);

        _matchStrategy.MatchAsync(Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(), Arg.Any<CancellationToken>())
            .Returns(MemberMatchResult.MultipleMatches());

        var command = new MemberMatchCommand(
            MemberPatient: patientNode,
            CoverageToMatch: coverageNode);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("multiple-matches");
    }

    #endregion

    #region Response Building Tests

    [Fact]
    public void GivenSuccessfulResult_WhenBuildingResponseParameters_ThenReturnsValidParametersResource()
    {
        // Arrange
        var memberIdentifier = new JsonObject
        {
            ["system"] = "http://example.org/members",
            ["value"] = "MEM12345"
        };

        var result = MemberMatchResult.Matched(memberIdentifier, "Patient/123");

        // Act
        var parameters = MemberMatchHandler.BuildResponseParameters(result);

        // Assert
        parameters.ResourceType.ShouldBe("Parameters");
        parameters.Parameter.ShouldNotBeNull();
        parameters.Parameter.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Verify MemberIdentifier parameter
        var memberIdParam = parameters.FindParameter("MemberIdentifier");
        memberIdParam.ShouldNotBeNull();
    }

    [Fact]
    public void GivenNoMatchResult_WhenBuildingErrorOperationOutcome_ThenReturnsValidOperationOutcome()
    {
        // Arrange
        var result = MemberMatchResult.NoMatch("No matching member found");

        // Act
        var outcome = MemberMatchHandler.BuildErrorOperationOutcome(result);

        // Assert
        outcome.ResourceType.ShouldBe("OperationOutcome");
        outcome.Issue.ShouldNotBeNull();
        outcome.Issue.Count.ShouldBeGreaterThanOrEqualTo(1);

        var firstIssue = outcome.Issue[0];
        firstIssue.Severity.ShouldBe(OperationOutcomeJsonNode.IssueSeverity.Error);
        firstIssue.Code.ShouldBe(OperationOutcomeJsonNode.IssueType.NotFound);
    }

    [Fact]
    public void GivenMultipleMatchesResult_WhenBuildingErrorOperationOutcome_ThenReturnsValidOperationOutcome()
    {
        // Arrange
        var result = MemberMatchResult.MultipleMatches("Multiple members found");

        // Act
        var outcome = MemberMatchHandler.BuildErrorOperationOutcome(result);

        // Assert
        outcome.ResourceType.ShouldBe("OperationOutcome");
        outcome.Issue.ShouldNotBeNull();

        var firstIssue = outcome.Issue[0];
        firstIssue.Severity.ShouldBe(OperationOutcomeJsonNode.IssueSeverity.Error);
        firstIssue.Code.ShouldBe(OperationOutcomeJsonNode.IssueType.MultipleMatches);
    }

    #endregion
}
