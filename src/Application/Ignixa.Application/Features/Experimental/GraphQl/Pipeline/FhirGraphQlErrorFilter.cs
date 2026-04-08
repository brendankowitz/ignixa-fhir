// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using Ignixa.Serialization.Models;
using static Ignixa.Serialization.Models.OperationOutcomeJsonNode;

namespace Ignixa.Application.Features.Experimental.GraphQl.Pipeline;

public sealed class FhirGraphQlErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        var issueType = MapToFhirIssueType(error.Code);

        // Include the original exception message when available for debuggability.
        // HC hides exception details behind "Unexpected Execution Error" by default.
        var diagnostics = error.Exception is not null
            ? $"{error.Message}: {error.Exception.GetType().Name}: {error.Exception.Message}"
            : error.Message;

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
        "HC0013" => IssueType.TooCostly,      // Max execution depth exceeded
        "HC0014" => IssueType.TooCostly,      // Execution timeout
        "AUTH_NOT_AUTHORIZED" => IssueType.Forbidden,
        _ => IssueType.Exception,
    };
}
