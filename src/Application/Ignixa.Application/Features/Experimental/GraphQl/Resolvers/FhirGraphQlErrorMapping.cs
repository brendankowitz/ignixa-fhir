// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

/// <summary>
/// Maps FHIR domain exceptions to coded <see cref="GraphQLException"/> instances so that
/// client errors surface with a meaningful error code instead of being laundered into a
/// generic "Unexpected Execution Error" by HotChocolate.
/// </summary>
internal static class FhirGraphQlErrorMapping
{
    /// <summary>
    /// Converts a <see cref="FhirException"/> into a coded <see cref="GraphQLException"/>,
    /// logging the failure at Warning. <paramref name="operation"/> is included in the log
    /// for diagnostic context (e.g. "Create Patient", "Search Observation").
    /// </summary>
    public static GraphQLException Map(FhirException exception, string operation, ILogger logger)
    {
        var code = exception is ResourceNotFoundException
            ? "FHIR_NOT_FOUND"
            : CodeFromStatus(exception.StatusCode);

        logger.LogWarning(
            exception,
            "GraphQL operation {Operation} failed with status {StatusCode} ({Code})",
            operation, exception.StatusCode, code);

        return new GraphQLException(
            ErrorBuilder.New()
                .SetMessage(exception.Message)
                .SetCode(code)
                .SetException(exception)
                .Build());
    }

    public static string CodeFromStatus(int statusCode) => statusCode switch
    {
        404 => "FHIR_NOT_FOUND",
        409 or 412 => "FHIR_VERSION_CONFLICT",
        400 => "INVALID_RESOURCE",
        _ => "FHIR_OPERATION_FAILED",
    };
}
