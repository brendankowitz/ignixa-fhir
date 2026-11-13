// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// A semantic <c>special</c> SearchParameter cannot be embedded for a resource because of how the
/// parameter -- or the server's <see cref="VectorSearchOptions.Indexing"/> default it falls back to --
/// is defined, not because of anything invalid in the resource itself: the FHIRPath-extracted index
/// value was not a FHIR string, the effective chunk size/overlap is out of the supported range, the
/// extraction policy is not one this server recognizes, or the text produced more chunks than
/// <see cref="VectorChunk.Ordinal"/> (a <see cref="short"/>) can address. A resource that is otherwise a
/// valid FHIR instance cannot be made to pass by changing it: the SearchParameter definition or
/// <c>VectorSearch</c> configuration needs fixing. Subclasses <see cref="FhirException"/> so
/// <c>FhirExceptionMiddleware</c> maps it to an HTTP response with no special-cased branch.
/// </summary>
/// <remarks>
/// Results in HTTP 500 Internal Server Error, the same as <see cref="EmbeddingProviderContractException"/>
/// (a distinct provider-response contract violation) -- not 400, which this codebase reserves for
/// problems the client can fix by changing the request.
/// </remarks>
public sealed class SemanticSearchDefinitionException : FhirException
{
    /// <inheritdoc />
    public override int StatusCode => 500;

    public SemanticSearchDefinitionException(string message)
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
