// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// The embedding provider responded, but its response did not match the request it was given: it
/// returned a different number of embeddings than passages submitted, or a vector whose dimensionality
/// does not equal <see cref="VectorSearchOptions.SupportedDimensions"/>. Distinct from
/// <see cref="EmbeddingUnavailableException"/> -- the provider was reachable and did not fail the call --
/// this is a contract violation between the configured deployment and what this server expects from it
/// (for example a deployment name that now points at a different model, or one returning a different
/// embedding size). Retrying the same request would not help; an operator must fix the provider
/// configuration. Subclasses <see cref="FhirException"/> so <c>FhirExceptionMiddleware</c> maps it to an
/// HTTP response with no special-cased branch, the same way every other per-condition status in this
/// codebase is expressed.
/// </summary>
/// <remarks>
/// Results in HTTP 500 Internal Server Error: nothing about the client's request or the resource being
/// written was invalid.
/// </remarks>
public sealed class EmbeddingProviderContractException : FhirException
{
    /// <inheritdoc />
    public override int StatusCode => 500;

    public EmbeddingProviderContractException(string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Issues.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Exception,
            Diagnostics = message
        });
    }
}
