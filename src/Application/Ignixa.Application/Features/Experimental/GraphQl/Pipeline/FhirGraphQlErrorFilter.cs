// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Ignixa.Models.OperationOutcomeIssue;

namespace Ignixa.Application.Features.Experimental.GraphQl.Pipeline;

public sealed class FhirGraphQlErrorFilter(
    ILogger<FhirGraphQlErrorFilter> logger,
    IOptions<ExperimentalOptions> options)
    : IErrorFilter
{
    private readonly bool _includeExceptionDetails =
        options.Value.Features.GraphQl.IncludeExceptionDetails;

    public IError OnError(IError error)
    {
        var issueType = MapToFhirIssueType(error.Code);
        var message = error.Message ?? "Unexpected GraphQL error.";

        if (issueType == IssueTypeCommon.Exception)
            logger.LogError(error.Exception, "Unexpected GraphQL error: {Message}", message);

        var diagnostics = _includeExceptionDetails && error.Exception is not null
            ? $"{message}: {error.Exception.GetType().Name}: {error.Exception.Message}"
            : message;

        var outcome = new OperationOutcome();
        outcome.Issue.Add(new OperationOutcomeIssue
        {
            SeverityCode = IssueSeverityCode.Error,
            IssueTypeCode = issueType,
            Diagnostics = diagnostics,
        });

        return ErrorBuilder.FromError(error)
            .SetExtension("resource", outcome.MutableNode)
            .Build();
    }

    private static IssueTypeCommon MapToFhirIssueType(string? errorCode) => errorCode switch
    {
        "FHIR_REFERENCE_NOT_FOUND" => IssueTypeCommon.NotFound,
        "FHIR_NOT_FOUND" => IssueTypeCommon.NotFound,
        "FHIR_REFERENCE_NOT_SUPPORTED" => IssueTypeCommon.NotSupported,
        "FHIR_VERSION_CONFLICT" => IssueTypeCommon.Conflict,
        "INVALID_RESOURCE" => IssueTypeCommon.Invalid,
        "FHIRPATH_INVALID" => IssueTypeCommon.Invalid,
        "FHIR_OPERATION_FAILED" => IssueTypeCommon.Exception,
        "FHIR_SINGLETON_VIOLATION" => IssueTypeCommon.MultipleMatches,
        "FHIR_SYNTAX_ERROR" => IssueTypeCommon.Invalid,
        "FHIR_INVALID_INSTANCE_QUERY" => IssueTypeCommon.Invalid,
        "FHIR_UNKNOWN_RESOURCE_TYPE" => IssueTypeCommon.NotSupported,
        "FHIR_INVALID_ID" => IssueTypeCommon.Invalid,
        "FHIR_POST_PROCESSING_FAILED" => IssueTypeCommon.Exception,
        "HC0013" => IssueTypeCommon.TooCostly,      // Max execution depth exceeded
        "HC0014" => IssueTypeCommon.TooCostly,      // Execution timeout
        "AUTH_NOT_AUTHORIZED" => IssueTypeCommon.Forbidden,
        _ => IssueTypeCommon.Exception,
    };
}
