// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Serialization.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Ignixa.Serialization.Models.OperationOutcomeJsonNode;

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

        if (issueType == IssueType.Exception)
            logger.LogError(error.Exception, "Unexpected GraphQL error: {Message}", message);

        var diagnostics = _includeExceptionDetails && error.Exception is not null
            ? $"{message}: {error.Exception.GetType().Name}: {error.Exception.Message}"
            : message;

        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new IssueComponent
        {
            Severity = IssueSeverity.Error,
            Code = issueType,
            Diagnostics = diagnostics,
        });

        return ErrorBuilder.FromError(error)
            .SetExtension("resource", outcome.MutableNode)
            .Build();
    }

    private static IssueType MapToFhirIssueType(string? errorCode) => errorCode switch
    {
        "FHIR_REFERENCE_NOT_FOUND" => IssueType.NotFound,
        "FHIR_NOT_FOUND" => IssueType.NotFound,
        "FHIR_REFERENCE_NOT_SUPPORTED" => IssueType.NotSupported,
        "FHIR_VERSION_CONFLICT" => IssueType.Conflict,
        "INVALID_RESOURCE" => IssueType.Invalid,
        "FHIRPATH_INVALID" => IssueType.Invalid,
        "FHIR_OPERATION_FAILED" => IssueType.Exception,
        "FHIR_SINGLETON_VIOLATION" => IssueType.MultipleMatches,
        "FHIR_SYNTAX_ERROR" => IssueType.Invalid,
        "FHIR_INVALID_INSTANCE_QUERY" => IssueType.Invalid,
        "FHIR_UNKNOWN_RESOURCE_TYPE" => IssueType.NotSupported,
        "FHIR_INVALID_ID" => IssueType.Invalid,
        "FHIR_POST_PROCESSING_FAILED" => IssueType.Exception,
        "HC0013" => IssueType.TooCostly,      // Max execution depth exceeded
        "HC0014" => IssueType.TooCostly,      // Execution timeout
        "AUTH_NOT_AUTHORIZED" => IssueType.Forbidden,
        _ => IssueType.Exception,
    };
}
