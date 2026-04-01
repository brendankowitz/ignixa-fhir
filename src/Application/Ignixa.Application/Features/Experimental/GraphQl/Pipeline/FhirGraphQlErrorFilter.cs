// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;

namespace Ignixa.Application.Features.Experimental.GraphQl.Pipeline;

public sealed class FhirGraphQlErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        var issueCode = MapToFhirIssueCode(error.Code);
        var severity = "error";

        // Include the original exception message when available for debuggability.
        // HC hides exception details behind "Unexpected Execution Error" by default.
        var diagnostics = error.Exception is not null
            ? $"{error.Message}: {error.Exception.GetType().Name}: {error.Exception.Message}"
            : error.Message;

        var operationOutcome = new Dictionary<string, object?>
        {
            ["resourceType"] = "OperationOutcome",
            ["issue"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["severity"] = severity,
                    ["code"] = issueCode,
                    ["diagnostics"] = diagnostics,
                },
            },
        };

        return ErrorBuilder.FromError(error)
            .SetExtension("resource", operationOutcome)
            .Build();
    }

    private static string MapToFhirIssueCode(string? errorCode) => errorCode switch
    {
        "FHIR_REFERENCE_NOT_FOUND" => "not-found",
        "HC0013" => "too-costly",      // Max execution depth exceeded
        "HC0014" => "too-costly",      // Execution timeout
        "AUTH_NOT_AUTHORIZED" => "forbidden",
        _ => "exception",
    };
}
