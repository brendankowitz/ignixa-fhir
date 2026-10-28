// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// The embedding provider could not be reached or failed transiently while <see cref="SemanticIndexer"/>
/// was embedding semantic text -- a connectivity, authentication, throttling or timeout failure, not a
/// problem with the request or the resource being written. Subclasses <see cref="FhirException"/> so
/// <c>FhirExceptionMiddleware</c> maps it to an HTTP response without any change to that middleware:
/// every other per-condition HTTP status in this codebase is expressed the same way (see
/// <c>Ignixa.Domain.Exceptions.PreconditionFailedException</c> et al.), rather than as a special case in
/// the exception-handling middleware itself.
/// </summary>
/// <remarks>
/// Results in HTTP 503 Service Unavailable: the write did not fail because the request or resource was
/// invalid, but because a dependency the server needs to finish the write was unavailable. A client
/// retrying later, once the provider recovers, is the correct remediation -- unlike the 4xx family this
/// codebase otherwise reserves for problems the client itself must fix before retrying.
/// </remarks>
public class EmbeddingUnavailableException : FhirException
{
    /// <inheritdoc />
    public override int StatusCode => 503;

    public EmbeddingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(innerException);

        Issues.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Transient,
            Diagnostics = message
        });
    }
}
